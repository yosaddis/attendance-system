using System.Security.Cryptography;
using System.Text;

namespace AttendanceApi.Services;

public static class StationKeyGenerator
{
    public static (string PlaintextKey, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = Convert.ToBase64String(bytes).Replace("=", "").Replace("+", "").Replace("/", "");
        return (plaintext, Hash(plaintext));
    }

    public static string Hash(string plaintextKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey));
        return Convert.ToHexString(bytes);
    }
}
