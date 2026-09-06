namespace VoiceCraft.Server.Android;

internal readonly record struct DiagnosticAdvice(string Title, string Cause, string Fix);

internal static class RuntimeDiagnostics
{
    internal static string TranslateSnapshot(string raw, bool thai)
    {
        if (!thai || string.IsNullOrWhiteSpace(raw))
            return raw;

        var output = new List<string>();
        foreach (var sourceLine in raw.Replace("\r\n", "\n").Split('\n'))
        {
            var line = TranslateLine(sourceLine);
            output.Add(line);

            if (sourceLine.Contains("Relay disconnected", StringComparison.OrdinalIgnoreCase) ||
                sourceLine.Contains("FATAL:", StringComparison.OrdinalIgnoreCase))
            {
                var advice = Describe(sourceLine, true);
                if (!string.IsNullOrWhiteSpace(advice.Fix))
                    output.Add($"    ↳ สาเหตุที่เป็นไปได้: {advice.Cause} | วิธีแก้: {advice.Fix}");
            }
        }
        return string.Join("\n", output);
    }

    private static string TranslateLine(string line)
    {
        var replacements = new (string From, string To)[]
        {
            (" UI: ", " หน้าจอ: "),
            (" SERVICE: ", " บริการ: "),
            (" BRIDGE: ", " บริดจ์: "),
            (" RUNTIME: ", " ระบบ: "),
            (" VOICE: ", " เสียง: "),
            (" SECURITY: ", " ความปลอดภัย: "),
            (" PROBE: ", " ตรวจสอบ: "),
            (" POWER: ", " พลังงาน: "),
            (" FATAL: ", " ข้อผิดพลาดร้ายแรง: "),
            ("MainActivity opened", "เปิดหน้าหลักของแอปแล้ว"),
            ("Log cleared", "ล้าง Log แล้ว"),
            ("START SERVER pressed", "กดปุ่มเริ่มเซิร์ฟเวอร์"),
            ("STOP SERVER pressed", "กดปุ่มหยุดเซิร์ฟเวอร์"),
            ("Starting foreground server on port", "กำลังเริ่มเซิร์ฟเวอร์เบื้องหลังที่พอร์ต"),
            ("Start ignored: server task is already active", "ไม่เริ่มซ้ำ เพราะเซิร์ฟเวอร์กำลังทำงานอยู่แล้ว"),
            ("Stop action received", "ได้รับคำสั่งหยุดเซิร์ฟเวอร์"),
            ("App data:", "โฟลเดอร์ข้อมูลแอป:"),
            ("Configured outbound relay=", "ตั้งค่า Relay ขาออก="),
            ("secret hidden", "ซ่อนค่า secret"),
            ("Phase 2 bridge disabled in app settings", "ปิด Bridge Phase 2 ในการตั้งค่าแอป"),
            ("Partial wake lock acquired", "เปิด Partial wake lock เพื่อช่วยให้เซิร์ฟเวอร์ทำงานต่อเนื่องแล้ว"),
            ("Wake lock unavailable:", "ไม่สามารถใช้ wake lock ได้:"),
            ("Initializing VoiceCraft v1.7.1 server runtime", "กำลังเริ่มระบบ VoiceCraft Server v1.7.1"),
            ("Requested UDP listener", "ขอเปิด UDP listener"),
            ("Requested HTTP listener", "ขอเปิด HTTP listener"),
            ("Login token loaded (value hidden from log)", "โหลด Login token แล้ว (ซ่อนค่าใน Log)"),
            ("VoiceCraft server loop ended normally", "วงจร VoiceCraft Server จบการทำงานตามปกติ"),
            ("Server task stopped", "งานเซิร์ฟเวอร์หยุดแล้ว"),
            ("Foreground service destroyed", "ปิด Foreground Service แล้ว"),
            ("Phase 2 starting relay=", "กำลังเริ่ม Bridge Phase 2 relay="),
            ("Attached to VoiceCraft world; entities=", "เชื่อม Bridge เข้ากับ VoiceCraft world แล้ว; entities="),
            ("Relay connected", "เชื่อมต่อ Relay แล้ว"),
            ("Relay disconnected", "Relay หลุดการเชื่อมต่อ"),
            ("retrying in 5s", "จะลองเชื่อมต่อใหม่ใน 5 วินาที"),
            ("Endstone peer connected", "Endstone เชื่อมต่อแล้ว"),
            ("Endstone peer disconnected", "Endstone หลุดการเชื่อมต่อ"),
            ("Endstone state sync received cached_players=", "รับข้อมูล Sync จาก Endstone แล้ว cached_players="),
            ("Voice client entity=", "Voice client entity="),
            ("awaiting /vcbind", "กำลังรอ /vcbind"),
            ("key hidden from log", "ซ่อน binding key จาก Log"),
            ("BIND success player=", "Bind สำเร็จ player="),
            ("BIND rejected player=", "Bind ถูกปฏิเสธ player="),
            ("UNBIND player=", "ยกเลิก Bind player="),
            ("TCP localhost attempt", "การตรวจ TCP localhost ครั้งที่"),
            ("connected (attempt", "เชื่อมต่อสำเร็จ (ครั้งที่"),
            ("McHttp raw HTTP response:", "ผลตอบกลับ McHttp แบบ raw HTTP:"),
            ("Server starting...", "กำลังเริ่มเซิร์ฟเวอร์..."),
            ("Starting VoiceCraft server...", "กำลังเริ่ม VoiceCraft server..."),
            ("Successfully started the VoiceCraft server!", "เริ่ม VoiceCraft server สำเร็จ!"),
            ("Starting McHttp server...", "กำลังเริ่ม McHttp server..."),
            ("Successfully started the McHttp server!", "เริ่ม McHttp server สำเร็จ!"),
            ("Registering commands...", "กำลังลงทะเบียนคำสั่ง..."),
            ("Successfully registered", "ลงทะเบียนสำเร็จ"),
            ("Server successfully started!", "เซิร์ฟเวอร์เริ่มทำงานสำเร็จ!"),
            ("Successfully loaded server properties!", "โหลดการตั้งค่าเซิร์ฟเวอร์สำเร็จ!"),
            ("Loading ServerProperties.json file at", "กำลังโหลด ServerProperties.json ที่"),
            ("failed:", "ล้มเหลว:"),
            ("error", "ข้อผิดพลาด")
        };

        foreach (var (from, to) in replacements)
            line = line.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        return line;
    }

    internal static DiagnosticAdvice Describe(string? error, bool thai)
    {
        var text = error ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return thai
                ? new DiagnosticAdvice("ไม่พบข้อผิดพลาด", "ไม่มีข้อมูลข้อผิดพลาด", string.Empty)
                : new DiagnosticAdvice("No error", "No error information", string.Empty);

        if (ContainsAny(text, "404", "ConnectStatusExpected") && text.Contains("WebSocket", StringComparison.OrdinalIgnoreCase))
            return thai
                ? new DiagnosticAdvice("WebSocket URL ไม่ถูกต้อง", "Relay ตอบ 404 เพราะ URL/Path ไม่ตรงกับ /bridge", "วาง Render Service URL ในหน้า Bridge แล้วให้แอปสร้าง wss://.../bridge ให้อัตโนมัติ")
                : new DiagnosticAdvice("Invalid WebSocket URL", "The relay returned 404 because the WebSocket path is wrong", "Paste the Render service URL in Bridge Setup and let the app generate wss://.../bridge");

        if (ContainsAny(text, "relay rejected hello", "401 Unauthorized", "403 Forbidden") && !text.Contains("McHttp raw HTTP response", StringComparison.OrdinalIgnoreCase))
            return thai
                ? new DiagnosticAdvice("Relay ปฏิเสธการยืนยันตัวตน", "Bridge Secret หรือ Server ID ไม่ตรงกันระหว่าง Render, Android และ Endstone", "ตรวจ BRIDGE_SECRET บน Render แล้วใช้ secret และ server_id เดียวกันทั้ง 3 ฝั่ง")
                : new DiagnosticAdvice("Relay authentication rejected", "Bridge Secret or Server ID does not match between Render, Android and Endstone", "Use the exact same BRIDGE_SECRET and server_id on all three sides");

        if (ContainsAny(text, "Address already in use", "EADDRINUSE", "Only one usage of each socket address"))
            return thai
                ? new DiagnosticAdvice("พอร์ตถูกใช้งานอยู่", "มีโปรแกรมหรือ VoiceCraft Server ตัวอื่นกำลังใช้พอร์ตเดียวกัน", "หยุดเซิร์ฟเวอร์ตัวเดิมก่อน หรือเปลี่ยนพอร์ตใน Settings แล้วลองใหม่")
                : new DiagnosticAdvice("Port already in use", "Another process or VoiceCraft server is using the same port", "Stop the other server or choose another port in Settings");

        if (ContainsAny(text, "Connection refused", "actively refused", "ECONNREFUSED"))
            return thai
                ? new DiagnosticAdvice("ปลายทางปฏิเสธการเชื่อมต่อ", "Render Relay อาจหยุดอยู่ หรือ URL/Port ผิด", "เปิด Render Dashboard ตรวจว่า Web Service เป็น Live และตรวจ URL ใน Bridge Setup")
                : new DiagnosticAdvice("Connection refused", "The Render relay may be stopped or the URL/port is wrong", "Check that the Render Web Service is Live and verify the Bridge URL");

        if (ContainsAny(text, "Name or service not known", "No such host", "NameResolution", "Temporary failure in name resolution", "nodename nor servname"))
            return thai
                ? new DiagnosticAdvice("ค้นหาโดเมนไม่สำเร็จ", "Android ไม่มีอินเทอร์เน็ต, DNS มีปัญหา หรือชื่อโดเมน Render ผิด", "ตรวจอินเทอร์เน็ต แล้วคัดลอก URL จากหน้า Render Service มาใส่ใหม่")
                : new DiagnosticAdvice("DNS lookup failed", "Android has no Internet connection, DNS failed, or the Render hostname is wrong", "Check Internet access and paste the Render service URL again");

        if (ContainsAny(text, "timed out", "TimeoutException", "timeout"))
            return thai
                ? new DiagnosticAdvice("การเชื่อมต่อหมดเวลา", "เครือข่ายช้า, Relay ไม่ตอบสนอง หรือปลายทางเข้าถึงไม่ได้", "ตรวจอินเทอร์เน็ต/Render แล้วรอให้ Relay Live ก่อนลองใหม่")
                : new DiagnosticAdvice("Connection timed out", "The network is slow, the relay is not responding, or the endpoint is unreachable", "Check Internet/Render status and retry when the relay is Live");

        if (ContainsAny(text, "invalid-config", "invalid URI", "UriFormatException", "URL/server id/secret is invalid"))
            return thai
                ? new DiagnosticAdvice("Bridge Config ไม่ครบ", "URL, Server ID หรือ Secret ไม่ผ่านการตรวจสอบ", "ไปหน้า Bridge Setup แล้วกรอกช่องที่มีกรอบสีแดงให้ครบ")
                : new DiagnosticAdvice("Bridge configuration incomplete", "URL, Server ID or Secret failed validation", "Open Bridge Setup and complete the red-highlighted fields");

        if (ContainsAny(text, "PlatformNotSupportedException", "not supported on this platform"))
            return thai
                ? new DiagnosticAdvice("ฟังก์ชันไม่รองรับบน Android", "Runtime พยายามใช้ API ที่ Android ไม่รองรับ", "คัดลอก Log ทั้งหมดเพื่อแก้ที่โค้ด Android runtime; หลีกเลี่ยงการใช้ APK เวอร์ชันเก่า")
                : new DiagnosticAdvice("Unsupported Android API", "The runtime attempted to use an API Android does not support", "Copy the full log for a runtime fix and avoid older APK builds");

        return thai
            ? new DiagnosticAdvice("เกิดข้อผิดพลาด", FirstLine(text), "เปิดหน้า Logs แล้วกดคัดลอก Log เพื่อดูรายละเอียด หากข้อผิดพลาดเกี่ยวกับ Bridge ให้ตรวจ Render URL, Server ID และ Secret ก่อน")
            : new DiagnosticAdvice("An error occurred", FirstLine(text), "Open Logs and copy the full log. For bridge errors, verify Render URL, Server ID and Secret first");
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string FirstLine(string text)
    {
        var first = text.Replace("\r\n", "\n").Split('\n', 2)[0].Trim();
        return first.Length > 220 ? first[..220] + "…" : first;
    }
}
