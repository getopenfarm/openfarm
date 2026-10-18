namespace OpenFarm.Domain.ConnectionSecrets;

public sealed class ConnectionSecret
{
    private ConnectionSecret(
        Guid id,
        string printerSerialNumber,
        string encryptedAccessCode,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        PrinterSerialNumber = printerSerialNumber;
        EncryptedAccessCode = encryptedAccessCode;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public string PrinterSerialNumber { get; }

    public string EncryptedAccessCode { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public static ConnectionSecret Create(string printerSerialNumber, string encryptedAccessCode)
    {
        if (string.IsNullOrWhiteSpace(printerSerialNumber))
        {
            throw new ArgumentException("Printer serial number is required.", nameof(printerSerialNumber));
        }

        if (string.IsNullOrWhiteSpace(encryptedAccessCode))
        {
            throw new ArgumentException("Encrypted access code is required.", nameof(encryptedAccessCode));
        }

        return new ConnectionSecret(Guid.NewGuid(), printerSerialNumber, encryptedAccessCode, DateTimeOffset.UtcNow);
    }
}
