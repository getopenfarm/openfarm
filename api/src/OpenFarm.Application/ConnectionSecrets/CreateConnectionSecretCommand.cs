namespace OpenFarm.Application.ConnectionSecrets;

public sealed record CreateConnectionSecretCommand(string PrinterSerialNumber, string PlainAccessCode);
