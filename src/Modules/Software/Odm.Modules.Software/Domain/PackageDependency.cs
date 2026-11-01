using Odm.SharedKernel;

namespace Odm.Modules.Software.Domain;

/// <summary>"The package needs another package": the other one is installed first and must be installed before this one starts.</summary>
public sealed class PackageDependency : Entity
{
    public const int MaxPerPackage = 20;

    public Guid PackageId { get; private set; }
    public Guid DependsOnId { get; private set; }

    private PackageDependency() { }

    public static PackageDependency Create(Guid packageId, Guid dependsOnId) => new() { PackageId = packageId, DependsOnId = dependsOnId };
}
