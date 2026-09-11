using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;

namespace VoiceCraft.Server.Android;

internal sealed class AccountSessionStore
{
    private const string PreferencesName = "voicecraft_account_session";
    private const string CiphertextKey = "ciphertext";
    private const string IvKey = "iv";
    private const string KeyAlias = "voicecraft_account_session_key_v1";

    private readonly Context _context;

    internal AccountSessionStore(Context context)
    {
        _context = context.ApplicationContext ?? context;
    }

    internal void Save(SupabaseAuthClient.AuthSession session)
    {
        var json = JsonSerializer.Serialize(session);
        var encrypted = Encrypt(Encoding.UTF8.GetBytes(json));
        var prefs = _context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        prefs?.Edit()
            ?.PutString(CiphertextKey, Convert.ToBase64String(encrypted.Ciphertext))
            ?.PutString(IvKey, Convert.ToBase64String(encrypted.Iv))
            ?.Apply();
    }

    internal SupabaseAuthClient.AuthSession? Load()
    {
        try
        {
            var prefs = _context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
            var encrypted = prefs?.GetString(CiphertextKey, null);
            var iv = prefs?.GetString(IvKey, null);
            if (string.IsNullOrWhiteSpace(encrypted) || string.IsNullOrWhiteSpace(iv)) return null;

            var plaintext = Decrypt(Convert.FromBase64String(encrypted), Convert.FromBase64String(iv));
            return JsonSerializer.Deserialize<SupabaseAuthClient.AuthSession>(Encoding.UTF8.GetString(plaintext));
        }
        catch
        {
            Clear();
            return null;
        }
    }

    internal void Clear()
    {
        _context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)?.Edit()?.Clear()?.Apply();
    }

    internal string GetOrCreateInstallationId()
    {
        const string installKey = "installation_id";
        var prefs = _context.GetSharedPreferences("voicecraft_device", FileCreationMode.Private);
        var existing = prefs?.GetString(installKey, null);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        var value = Guid.NewGuid().ToString("N");
        prefs?.Edit()?.PutString(installKey, value)?.Apply();
        return value;
    }

    private static (byte[] Ciphertext, byte[] Iv) Encrypt(byte[] plaintext)
    {
        using var keyStore = KeyStore.GetInstance("AndroidKeyStore");
        keyStore.Load(null);
        EnsureKey(keyStore);
        using var key = keyStore.GetKey(KeyAlias, null);
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding");
        cipher.Init(CipherMode.EncryptMode, key);
        return (cipher.DoFinal(plaintext), cipher.GetIV() ?? Array.Empty<byte>());
    }

    private static byte[] Decrypt(byte[] ciphertext, byte[] iv)
    {
        using var keyStore = KeyStore.GetInstance("AndroidKeyStore");
        keyStore.Load(null);
        EnsureKey(keyStore);
        using var key = keyStore.GetKey(KeyAlias, null);
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding");
        using var spec = new GCMParameterSpec(128, iv);
        cipher.Init(CipherMode.DecryptMode, key, spec);
        return cipher.DoFinal(ciphertext);
    }

    private static void EnsureKey(KeyStore keyStore)
    {
        if (keyStore.ContainsAlias(KeyAlias)) return;

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore");
        using var spec = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetRandomizedEncryptionRequired(true)
            .Build();
        generator.Init(spec);
        generator.GenerateKey();
    }
}
