namespace AlzaBox.Planner.Core.Domain;

/// <summary>
/// Jedna naložená dodávka. Drží zbývající kapacitu v obou rozměrech a indexy naložených zásilek.
/// </summary>
public sealed class Van
{
    private readonly FleetCapacity _capacity;
    private readonly List<int> _packageIndices = [];

    public Van(int index, FleetCapacity capacity)
    {
        Index = index;
        _capacity = capacity;
        RemainingVolumeM3 = capacity.VanVolumeM3;
        RemainingWeightKg = capacity.VanWeightKg;
    }

    /// <summary>Pořadové číslo dodávky v rámci okruhu (0..119).</summary>
    public int Index { get; }

    public double RemainingVolumeM3 { get; private set; }

    public double RemainingWeightKg { get; private set; }

    public double RevenueCzk { get; private set; }

    /// <summary>Indexy zásilek do vstupního pole, které jsou naloženy v této dodávce.</summary>
    public IReadOnlyList<int> PackageIndices => _packageIndices;

    public double VolumeUtilization => 1.0 - RemainingVolumeM3 / _capacity.VanVolumeM3;

    public double WeightUtilization => 1.0 - RemainingWeightKg / _capacity.VanWeightKg;

    public bool CanFit(in Package package)
        => package.VolumeM3 <= RemainingVolumeM3 && package.WeightKg <= RemainingWeightKg;

    public void Load(int packageIndex, in Package package)
    {
        _packageIndices.Add(packageIndex);
        RemainingVolumeM3 -= package.VolumeM3;
        RemainingWeightKg -= package.WeightKg;
        RevenueCzk += package.RevenueCzk;
    }
}
