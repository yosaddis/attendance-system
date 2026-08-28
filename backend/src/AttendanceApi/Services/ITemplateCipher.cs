namespace AttendanceApi.Services;

public interface ITemplateCipher
{
    byte[] Encrypt(byte[] plaintext);
    byte[] Decrypt(byte[] ciphertext);
}
