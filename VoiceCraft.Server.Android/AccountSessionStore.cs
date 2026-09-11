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
        using var keyStore = GetKeyStore();
        EnsureKey(keyStore);
        using var key = keyStore.GetKey(KeyAlias, null)
            ?? throw new InvalidOperationException("Android Keystore did not return the account session key.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("AES-GCM cipher is unavailable on this Android device.");
        cipher.Init(CipherMode.EncryptMode, key);
        var ciphertext = cipher.DoFinal(plaintext)
            ?? throw new InvalidOperationException("Android Keystore encryption returned no data.");
        var iv = cipher.GetIV();
        if (iv is null || iv.Length == 0)
            throw new InvalidOperationException("Android Keystore encryption returned no IV.");
        return (ciphertext, iv);
    }

    private static byte[] Decrypt(byte[] ciphertext, byte[] iv)
    {
        using var keyStore = GetKeyStore();
        EnsureKey(keyStore);
        using var key = keyStore.GetKey(KeyAlias, null)
            ?? throw new InvalidOperationException("Android Keystore did not return the account session key.");
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("AES-GCM cipher is unavailable on this Android device.");
        using var spec = new GCMParameterSpec(128, iv);
        cipher.Init(CipherMode.DecryptMode, key, spec);
        return cipher.DoFinal(ciphertext)
            ?? throw new InvalidOperationException("Android Keystore decryption returned no data.");
    }

    private static KeyStore GetKeyStore()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")
            ?? throw new InvalidOperationException("Android Keystore is unavailable on this device.");
        keyStore.Load(null);
        return keyStore;
    }

    private static void EnsureKey(KeyStore keyStore)
    {
        if (keyStore.ContainsAlias(KeyAlias)) return;

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")
            ?? throw new InvalidOperationException("Android Keystore AES key generator is unavailable.");
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
