namespace OpenFarm.Api.Processors;

public interface IActivityLoggable
{
    string EntityType { get; }

    string EventType { get; }
}
