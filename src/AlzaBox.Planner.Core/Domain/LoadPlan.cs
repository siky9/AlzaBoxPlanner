using AlzaBox.Planner.Core.Selection;

namespace AlzaBox.Planner.Core.Domain;

/// <summary>
/// Nakládací plán jednoho okruhu – co poveze která dodávka.
/// </summary>
public sealed class LoadPlan
{
    public required FleetCapacity Capacity { get; init; }

    public required IReadOnlyList<Van> Vans { get; init; }

    /// <summary>Výsledek výběrové fáze včetně horní meze výnosnosti.</summary>
    public required SelectionResult Selection { get; init; }

    public required int LoadedPackageCount { get; init; }

    public required double RevenueCzk { get; init; }

    public required double VolumeM3 { get; init; }

    public required double WeightKg { get; init; }

    /// <summary>
    /// Zásilky, které výběr doporučil, ale nevešly se do žádné dodávky. Díky drobnosti zásilek
    /// oproti kapacitě dodávky jich bývá nula; dosypávací fáze je nahradí jinými.
    /// </summary>
    public required int UnplacedFromSelectionCount { get; init; }

    /// <summary>Zásilky přidané dosypávací fází do zbytkové kapacity dodávek.</summary>
    public required int TopUpCount { get; init; }

    public double VolumeUtilization => VolumeM3 / Capacity.TotalVolumeM3;

    public double WeightUtilization => WeightKg / Capacity.TotalWeightKg;

    public int UsedVanCount => Vans.Count(van => van.PackageIndices.Count > 0);

    /// <summary>Kolik procent výnosnosti nám nejvýš uniklo proti teoretickému optimu.</summary>
    public double GapPercent => Selection.UpperBoundCzk > 0
        ? 100.0 * (Selection.UpperBoundCzk - RevenueCzk) / Selection.UpperBoundCzk
        : 0.0;
}
