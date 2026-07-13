namespace OpenFarm.Domain.Printers;

public enum PrinterBrand
{
    Bambu,
}

public enum CameraTransport
{
    None,
    Rtsp,
    BinaryImage,
}

public sealed record PrinterModel(PrinterBrand Brand, string ModelCode, CameraTransport CameraTransport);
