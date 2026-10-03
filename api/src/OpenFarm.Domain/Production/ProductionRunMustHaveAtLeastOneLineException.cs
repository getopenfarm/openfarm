namespace OpenFarm.Domain.Production;

public sealed class ProductionRunMustHaveAtLeastOneLineException()
    : Exception("A production run must have at least one line.");
