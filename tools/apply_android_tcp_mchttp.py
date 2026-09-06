#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path


def replace_region(text: str, start_marker: str, end_marker: str, replacement: str) -> str:
    start = text.find(start_marker)
    if start < 0:
        raise RuntimeError(f"Start marker not found: {start_marker[:120]}")
    end = text.find(end_marker, start)
    if end < 0:
        raise RuntimeError(f"End marker not found: {end_marker[:120]}")
    return text[:start] + replacement.rstrip() + "\n\n" + text[end:]


def replace_once(text: str, old: str, new: str) -> str:
    if old not in text:
        raise RuntimeError(f"Expected block not found:\n{old[:240]}")
    return text.replace(old, new, 1)


def main() -> None:
    parser = argparse.ArgumentParser(description="Replace HttpListener McHttp with Android-compatible TcpListener HTTP transport")
    parser.add_argument("repo", nargs="?", default="VoiceCraft.Upstream")
    args = parser.parse_args()

    path = Path(args.repo).resolve() / "VoiceCraft.Network/Servers/HttpMcApiServer.cs"
    if not path.exists():
        raise SystemExit(f"HttpMcApiServer.cs not found: {path}")

    text = path.read_text(encoding="utf-8-sig")

    text = replace_once(text, "using System.Linq;\nusing System.Net;\n", "using System.Linq;\nusing System.IO;\nusing System.Net;\nusing System.Net.Sockets;\n")

    text = replace_once(
        text,
        "    private const int MaxRequestLength = 1_000_000;\n",
        "    private const int MaxRequestLength = 1_000_000;\n"
        "    private const int MaxHeaderLength = 32 * 1024;\n\n"
        "    /// <summary>Optional non-secret diagnostics sink used by embedded hosts.</summary>\n"
        "    public static Action<string>? DiagnosticLog { get; set; }\n"
    )

    text = replace_once(
        text,
        "    private HttpListener? _httpServer;\n",
        "    private TcpListener? _tcpServer;\n"
        "    private CancellationTokenSource? _listenerCts;\n"
    )

    text = replace_once(text, "            if (_httpServer != null)\n", "            if (_tcpServer != null)\n")

    text = replace_region(
        text,
        "    public override void Start()\n",
        "    public override void Update()\n",
        r'''    public override void Start()
    {
        IPEndPoint endpoint;
        CancellationToken token;
        lock (_lock)
        {
            Stop();
            endpoint = BuildTcpEndPoint(_config.Hostname);
            _listenerCts = new CancellationTokenSource();
            token = _listenerCts.Token;
            _tcpServer = new TcpListener(endpoint);
            _tcpServer.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _tcpServer.Start((int)Math.Clamp((long)Config.MaxClients, 1, 512));
        }

        DiagnosticLog?.Invoke($"McHttp TCP LISTENING {endpoint.Address}:{endpoint.Port} (raw HTTP/1.1)");
        _ = ListenerLoopAsync(_tcpServer, token);
    }'''
    )

    text = replace_region(
        text,
        "    public override void Update()\n",
        "    public override void Stop()\n",
        r'''    public override void Update()
    {
        var snapshot = _peersSnapshot;
        if (_tcpServer == null) return;
        foreach (var peer in snapshot.Cast<HttpMcApiNetPeer>()) UpdatePeer(peer);
    }'''
    )

    text = replace_region(
        text,
        "    public override void Stop()\n",
        "    public override void SendPacket<T>(McApiNetPeer netPeer, T packet)\n",
        r'''    public override void Stop()
    {
        lock (_lock)
        {
            try
            {
                _listenerCts?.Cancel();
            }
            catch
            {
                // Ignore cancellation errors during shutdown.
            }

            try
            {
                _tcpServer?.Stop();
            }
            catch
            {
                // Ignore socket shutdown errors.
            }

            _tcpServer = null;
            _listenerCts?.Dispose();
            _listenerCts = null;
            ClearHttpPeers();
        }
    }'''
    )

    # Remaining liveness checks in SendPacket/Broadcast/connect wait loop.
    text = text.replace("_httpServer", "_tcpServer")

    text = replace_region(
        text,
        "    private async Task ListenerLoopAsync(HttpListener listener)\n",
        "    private async Task<HttpMcApiNetPeer> HandleConnectRequestAsync(List<byte[]> packets)\n",
        r'''    private async Task ListenerLoopAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleTcpClientAsync(client, token), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                DiagnosticLog?.Invoke($"McHttp LISTENER ERROR {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client)
        {
            client.NoDelay = true;
            var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            try
            {
                using var stream = client.GetStream();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp((long)Config.MaxTimeoutMs, 1000, 30000)));
                var token = timeoutCts.Token;

                var request = await ReadRawHttpRequestAsync(stream, remote, token);
                if (request == null)
                    return;

                DiagnosticLog?.Invoke($"McHttp RX {request.Method} {request.Path} from {remote}");

                if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteRawHttpResponseAsync(stream, 403, ReadOnlyMemory<byte>.Empty, token);
                    return;
                }

                if (request.ContentLength < 0)
                {
                    await WriteRawHttpResponseAsync(stream, 411, ReadOnlyMemory<byte>.Empty, token);
                    return;
                }

                if (request.ContentLength > MaxRequestLength)
                {
                    await WriteRawHttpResponseAsync(stream, 413, ReadOnlyMemory<byte>.Empty, token);
                    return;
                }

                var packets = new List<byte[]>();
                var stringData = Encoding.UTF8.GetString(request.Body);
                if (!TryReadPackedPackets(stringData, packets))
                {
                    DiagnosticLog?.Invoke("McHttp REJECT 400 invalid packed payload");
                    await WriteRawHttpResponseAsync(stream, 400, ReadOnlyMemory<byte>.Empty, token);
                    return;
                }

                request.Headers.TryGetValue("Authorization", out var authorizationHeader);
                var sessionToken = ParseAuthorizationToken(authorizationHeader);
                HttpMcApiNetPeer? netPeer;

                if (IsConnectPath(request.Path))
                {
                    DiagnosticLog?.Invoke($"McHttp CONNECT packets={packets.Count}");
                    netPeer = await HandleConnectRequestAsync(packets);
                    DiagnosticLog?.Invoke($"McHttp CONNECT result={netPeer.ConnectionState}");
                }
                else if (string.IsNullOrWhiteSpace(sessionToken) || !TryGetHttpPeer(sessionToken, out netPeer))
                {
                    DiagnosticLog?.Invoke("McHttp REJECT 401 missing/invalid session token");
                    await WriteRawHttpResponseAsync(stream, 401, ReadOnlyMemory<byte>.Empty, token);
                    return;
                }
                else
                {
                    ReceivePacketsLogic(netPeer, packets, sessionToken);
                }

                packets.Clear();
                var response = BuildResponsePayload(netPeer, packets);
                DiagnosticLog?.Invoke($"McHttp TX 200 packets={packets.Count} bytes={response.Length}");
                await WriteRawHttpResponseAsync(stream, 200, response, token);
            }
            catch (OperationCanceledException)
            {
                DiagnosticLog?.Invoke($"McHttp REQUEST TIMEOUT from {remote}");
            }
            catch (Exception ex)
            {
                DiagnosticLog?.Invoke($"McHttp ERROR {ex.GetType().Name}: {ex.Message}");
                try
                {
                    if (client.Connected)
                    {
                        using var stream = client.GetStream();
                        await WriteRawHttpResponseAsync(stream, 500, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
                    }
                }
                catch
                {
                    // Ignore secondary response failures.
                }
            }
        }
    }

    private static async Task<RawHttpRequest?> ReadRawHttpRequestAsync(
        NetworkStream stream,
        string remote,
        CancellationToken token)
    {
        var rented = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var received = new MemoryStream();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(rented.AsMemory(0, rented.Length), token);
                if (read <= 0)
                    return null;

                received.Write(rented, 0, read);
                if (received.Length > MaxHeaderLength)
                    throw new InvalidDataException($"HTTP header too large from {remote}");

                var raw = received.GetBuffer();
                headerEnd = FindHeaderEnd(raw, checked((int)received.Length));
            }

            var all = received.ToArray();
            var bodyStart = headerEnd + 4;
            var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            if (lines.Length == 0)
                throw new InvalidDataException("Missing HTTP request line");

            var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestLine.Length < 2)
                throw new InvalidDataException("Malformed HTTP request line");

            var method = requestLine[0];
            var rawTarget = requestLine[1];
            var path = rawTarget;
            if (Uri.TryCreate(rawTarget, UriKind.Absolute, out var absoluteUri))
                path = absoluteUri.AbsolutePath;
            else
            {
                var queryIndex = path.IndexOf('?');
                if (queryIndex >= 0)
                    path = path[..queryIndex];
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var separator = lines[i].IndexOf(':');
                if (separator <= 0) continue;
                var name = lines[i][..separator].Trim();
                var value = lines[i][(separator + 1)..].Trim();
                headers[name] = value;
            }

            long contentLength = -1;
            if (headers.TryGetValue("Content-Length", out var contentLengthText))
            {
                if (!long.TryParse(contentLengthText, out contentLength) || contentLength < 0)
                    throw new InvalidDataException("Invalid Content-Length");
                if (contentLength > MaxRequestLength)
                {
                    return new RawHttpRequest(method, path, headers, Array.Empty<byte>(), contentLength);
                }
            }

            if (contentLength < 0)
                return new RawHttpRequest(method, path, headers, Array.Empty<byte>(), contentLength);

            var bodyLength = checked((int)contentLength);
            var body = new byte[bodyLength];
            var alreadyBuffered = Math.Min(bodyLength, Math.Max(0, all.Length - bodyStart));
            if (alreadyBuffered > 0)
                Buffer.BlockCopy(all, bodyStart, body, 0, alreadyBuffered);

            if (alreadyBuffered < bodyLength)
                await stream.ReadExactlyAsync(body.AsMemory(alreadyBuffered, bodyLength - alreadyBuffered), token);

            return new RawHttpRequest(method, path, headers, body, contentLength);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int FindHeaderEnd(byte[] data, int length)
    {
        for (var i = 0; i <= length - 4; i++)
        {
            if (data[i] == (byte)'\r' && data[i + 1] == (byte)'\n' &&
                data[i + 2] == (byte)'\r' && data[i + 3] == (byte)'\n')
                return i;
        }

        return -1;
    }

    private static async Task WriteRawHttpResponseAsync(
        NetworkStream stream,
        int statusCode,
        ReadOnlyMemory<byte> body,
        CancellationToken token)
    {
        var reason = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            411 => "Length Required",
            413 => "Payload Too Large",
            500 => "Internal Server Error",
            _ => "Error"
        };

        var header =
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            "Content-Type: text/plain; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "Cache-Control: no-store\r\n" +
            "\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, token);
        if (!body.IsEmpty)
            await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }

    private sealed class RawHttpRequest(
        string method,
        string path,
        Dictionary<string, string> headers,
        byte[] body,
        long contentLength)
    {
        public string Method { get; } = method;
        public string Path { get; } = path;
        public Dictionary<string, string> Headers { get; } = headers;
        public byte[] Body { get; } = body;
        public long ContentLength { get; } = contentLength;
    }'''
    )

    text = replace_region(
        text,
        "    private void SendPacketsLogic(HttpListenerContext context, HttpMcApiNetPeer netPeer, List<byte[]> packets)\n",
        "    private bool TryReadPackedPackets(string stringData, List<byte[]> packets)\n",
        r'''    private byte[] BuildResponsePayload(HttpMcApiNetPeer netPeer, List<byte[]> packets)
    {
        while (netPeer.OutgoingQueue.TryDequeue(out var packet))
        {
            try
            {
                packets.Add(packet.Data);
            }
            catch
            {
                // Do nothing.
            }
        }

        string encoded;
        lock (_httpWriter)
        {
            _httpWriter.Reset();
            foreach (var packet in packets)
            {
                _httpWriter.Put((ushort)packet.Length);
                _httpWriter.Put(packet);
            }

            encoded = Z85.GetStringWithPadding(_httpWriter.AsReadOnlySpan());
        }

        return Encoding.UTF8.GetBytes(encoded);
    }'''
    )

    text = replace_region(
        text,
        "    private static string BuildListenerPrefix(string configuredHostname)\n",
        "    private static string? ParseAuthorizationToken(string? authorizationHeader)\n",
        r'''    private static IPEndPoint BuildTcpEndPoint(string configuredHostname)
    {
        if (!Uri.TryCreate(configuredHostname, UriKind.Absolute, out var uri))
            throw new InvalidOperationException(
                $"Invalid McHttp hostname '{configuredHostname}'. Expected format like 'http://0.0.0.0:9050/'.");
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Invalid McHttp hostname '{configuredHostname}'. Only 'http://' is supported.");

        var port = uri.IsDefaultPort ? 80 : uri.Port;
        if (port is < 1 or > 65535)
            throw new InvalidOperationException($"Invalid McHttp port '{port}' in hostname '{configuredHostname}'.");

        IPAddress address;
        if (uri.Host is "0.0.0.0" or "+" or "*")
        {
            address = IPAddress.Any;
        }
        else if (!IPAddress.TryParse(uri.Host, out address!))
        {
            address = Dns.GetHostAddresses(uri.Host)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new InvalidOperationException($"McHttp hostname '{uri.Host}' did not resolve to an IPv4 address.");
        }

        return new IPEndPoint(address, port);
    }'''
    )

    if "HttpListener" in text:
        raise RuntimeError("Android TcpListener patch incomplete: HttpListener references remain")

    path.write_text(text, encoding="utf-8")
    print("Android TcpListener McHttp transport patch applied.")


if __name__ == "__main__":
    main()
