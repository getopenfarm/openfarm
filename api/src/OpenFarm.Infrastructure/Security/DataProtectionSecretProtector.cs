using Microsoft.AspNetCore.DataProtection;
using OpenFarm.Application.Common;

namespace OpenFarm.Infrastructure.Security;

public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "OpenFarm.ConnectionSecrets.AccessCode";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
