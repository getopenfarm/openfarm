namespace OpenFarm.Application.Common;

public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}
