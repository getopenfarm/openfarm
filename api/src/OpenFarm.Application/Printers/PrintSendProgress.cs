namespace OpenFarm.Application.Printers;

/// <summary>Where sending a job to its printer is. Live feedback only — never persisted.</summary>
public enum PrintSendStage
{
    Slicing,
    Uploading,
    Starting,
    Retrying,
    Sent,

    /// <summary>The attempt ended without sending; the job itself (its block reason) says why.</summary>
    Ended,
}

/// <param name="Percent">Of the upload, 0–100; null outside <see cref="PrintSendStage.Uploading"/>.</param>
public sealed record PrintSendProgress(PrintSendStage Stage, int? Percent = null);
