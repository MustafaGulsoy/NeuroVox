using System.Security.Cryptography;
using System.Text;

namespace NeuroVox.WebApi.Services
{
    // AES-256-GCM. Output: base64(nonce[12] | tag[16] | ciphertext). `context` (e.g. the row id) is authenticated,
    // so a ciphertext copied onto another row fails to decrypt.
    // The key comes from NeuroVox:SecretKey (base64, 32 bytes) -- never from the database that holds the ciphertext.
    // ponytail: single key, no rotation; add a key-id prefix byte when you need to rotate.
    public class SecretProtector
    {
        private readonly byte[]? _key;

        public SecretProtector(IConfiguration config)
        {
            var raw = config["NeuroVox:SecretKey"];
            if (string.IsNullOrWhiteSpace(raw)) return;
            var key = Convert.FromBase64String(raw);
            if (key.Length != 32) throw new InvalidOperationException("NeuroVox:SecretKey must be 32 bytes, base64 encoded");
            _key = key;
        }

        public bool Enabled => _key is not null;

        public string Protect(string plain, string context)
        {
            if (_key is null) throw new InvalidOperationException("NeuroVox:SecretKey is not configured");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var data = Encoding.UTF8.GetBytes(plain);
            var cipher = new byte[data.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(context));
            return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
        }

        public string Unprotect(string protectedText, string context)
        {
            if (_key is null) throw new InvalidOperationException("NeuroVox:SecretKey is not configured");
            var all = Convert.FromBase64String(protectedText);
            var plain = new byte[all.Length - 28];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(all.AsSpan(0, 12), all.AsSpan(28), all.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(context));
            return Encoding.UTF8.GetString(plain);
        }
    }
}
