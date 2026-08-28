using AttendanceApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AttendanceApi.Tests;

public class AesTemplateCipherTests
{
    private static AesTemplateCipher CreateCipher()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Templates:EncryptionKey"] = "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=",
            })
            .Build();
        return new AesTemplateCipher(config);
    }

    [Fact]
    public void Decrypt_ReturnsOriginalPlaintext_AfterEncrypt()
    {
        var cipher = CreateCipher();
        var plaintext = new byte[] { 10, 20, 30, 40, 50, 255, 0, 128 };

        var ciphertext = cipher.Encrypt(plaintext);
        var decrypted = cipher.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesCiphertextDifferentFromPlaintext()
    {
        var cipher = CreateCipher();
        var plaintext = new byte[] { 1, 2, 3, 4, 5 };

        var ciphertext = cipher.Encrypt(plaintext);

        // Ciphertext is IV (16 bytes) + encrypted bytes, so it's longer than plaintext,
        // and the trailing encrypted bytes must not equal the plaintext.
        Assert.NotEqual(plaintext, ciphertext);
        var encryptedPortion = ciphertext[16..];
        Assert.NotEqual(plaintext, encryptedPortion);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertext_ForSamePlaintextAcrossCalls()
    {
        var cipher = CreateCipher();
        var plaintext = new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 };

        var ciphertext1 = cipher.Encrypt(plaintext);
        var ciphertext2 = cipher.Encrypt(plaintext);

        // Fresh random IV each call means the full blobs (IV || ciphertext) must differ,
        // and specifically the IV (first 16 bytes) must differ between calls.
        Assert.NotEqual(ciphertext1, ciphertext2);
        var iv1 = ciphertext1[..16];
        var iv2 = ciphertext2[..16];
        Assert.NotEqual(iv1, iv2);

        // Both must still decrypt back to the same original plaintext.
        Assert.Equal(plaintext, cipher.Decrypt(ciphertext1));
        Assert.Equal(plaintext, cipher.Decrypt(ciphertext2));
    }

    [Fact]
    public void Encrypt_RoundTrips_EmptyPlaintext()
    {
        var cipher = CreateCipher();
        var plaintext = Array.Empty<byte>();

        var ciphertext = cipher.Encrypt(plaintext);
        var decrypted = cipher.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
        // PKCS7 padding still emits a full padding block even for empty input,
        // plus the 16-byte IV prefix, so the ciphertext is never empty.
        Assert.NotEmpty(ciphertext);
    }
}
