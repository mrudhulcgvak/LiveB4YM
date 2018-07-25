namespace Tar.Cryptography
{
    public interface IEncryption
    {
        string Encrypt(string input);
        string Decrypt(string input);
    }
}
