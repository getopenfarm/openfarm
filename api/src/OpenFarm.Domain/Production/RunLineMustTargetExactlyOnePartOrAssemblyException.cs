namespace OpenFarm.Domain.Production;

public sealed class RunLineMustTargetExactlyOnePartOrAssemblyException()
    : Exception("A run line must target exactly one Part or Assembly, never both or neither.");
