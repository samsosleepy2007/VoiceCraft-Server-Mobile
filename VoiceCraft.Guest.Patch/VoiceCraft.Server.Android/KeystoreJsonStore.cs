using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace VoiceCraft.Server.Android;

internal sealed class KeystoreJsonStore
{
    private sealed class Envelope
    {
        public int Version { get; set; } = 1;
        public string Iv { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
    }

    private readonly string _alias;
    private readonly string _fileName;
    private readonly object _sync = new();

    public KeystoreJsonStore(string alias, string fileName)
    {
        _alias = alias;
        _fileName = fileName;
    }

    public void Save<T>(Context context, T value)
    {
        lock (_sync)
        {
            var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
                ?? throw new InvalidOperationException("AES/GCM cipher is unavailable.");

            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, GetOrCreateKey());
            var encrypted = cipher.DoFinal(plaintext)
                ?? throw new InvalidOperationException("Encryption failed.");

            var envelope = new Envelope
            {
                Iv = Convert.ToBase64String(cipher.GetIV() ?? Array.Empty<byte>()),
                Ciphertext = Convert.ToBase64String(encrypted)
            };

            var path = PathFor(context);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(envelope));
            File.Move(temp, path, true);
        }
    }

    public bool TryLoad<T>(Context context, out T? value)
    {
        lock (_sync)
        {
            value = default;
            var path = PathFor(context);
            if (!File.Exists(path))
                return false;

            try
            {
                var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path));
                if (envelope == null || envelope.Version != 1)
                    return false;

                var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
                    ?? throw new InvalidOperationException("AES/GCM cipher is unavailable.");

                using var spec = new GCMParameterSpec(
                    128,
                    Convert.FromBase64String(envelope.Iv));

                cipher.Init(
                    Javax.Crypto.CipherMode.DecryptMode,
                    GetOrCreateKey(),
                    spec);

                var plaintext = cipher.DoFinal(
                    Convert.FromBase64String(envelope.Ciphertext));

                if (plaintext == null)
                    return false;

                value = JsonSerializer.Deserialize<T>(
                    Encoding.UTF8.GetString(plaintext));

                return value != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Delete(Context context)
    {
        lock (_sync)
        {
            try
            {
                var path = PathFor(context);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }

            try
            {
                using var keyStore = KeyStore.GetInstance("AndroidKeyStore");
                keyStore?.Load(null);
                if (keyStore?.ContainsAlias(_alias) == true)
                    keyStore.DeleteEntry(_alias);
            }
            catch { }
        }
    }

    private string PathFor(Context context)
    {
        var dir = context.NoBackupFilesDir?.AbsolutePath
            ?? context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("Android app storage is unavailable.");

        return Path.Combine(dir, _fileName);
    }

    private Javax.Crypto.ISecretKey GetOrCreateKey()
    {
        using var keyStore = KeyStore.GetInstance("AndroidKeyStore")
            ?? throw new InvalidOperationException("Android Keystore is unavailable.");

        keyStore.Load(null);

        if (!keyStore.ContainsAlias(_alias))
        {
            using var generator = KeyGenerator.GetInstance(
                KeyProperties.KeyAlgorithmAes,
                "AndroidKeyStore")
                ?? throw new InvalidOperationException(
                    "Android Keystore AES generator is unavailable.");

            using var spec = new KeyGenParameterSpec.Builder(
                    _alias,
                    KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetRandomizedEncryptionRequired(true)
                .Build();

            generator.Init(spec);
            generator.GenerateKey();
        }

        var entry = keyStore.GetEntry(_alias, null)
            as KeyStore.SecretKeyEntry
            ?? throw new InvalidOperationException(
                "Android Keystore entry is unavailable.");

        return entry.SecretKey;
    }
}
