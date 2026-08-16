using System.Linq;
using System.Security.Cryptography;

namespace AttendanceApi.Services;

public class AesTemplateCipher : ITemplateCipher
{
    private readonly byte[] _key;

    public AesTemplateCipher(IConfiguration config)
    {
        _key = Convert.FromBase64String(config["Templates:EncryptionKey"]!);
    }

    public byte[] Encrypt(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        return aes.IV.Concat(cipherBytes).ToArray();
    }

    public byte[] Decrypt(byte[] ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = ciphertext[..16];
        var cipherBytes = ciphertext[16..];
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }
}
