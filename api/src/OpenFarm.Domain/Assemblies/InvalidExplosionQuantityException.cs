namespace OpenFarm.Domain.Assemblies;

/// <summary>FR-009e: explosion was requested at a quantity below 1 or non-integer.</summary>
public sealed class InvalidExplosionQuantityException(int quantity)
    : Exception($"Explosion quantity must be a whole number of at least 1; got {quantity}.")
{
    public int Quantity { get; } = quantity;
}
