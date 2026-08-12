namespace AlzaBox.Planner.Core.Domain;

/// <summary>
/// Kapacita flotily pro <b>jeden okruh</b> (jedno plánování). Všechny dodávky jsou identické.
/// </summary>
/// <param name="VanCount">Počet dodávek vyjíždějících na okruh.</param>
/// <param name="VanVolumeM3">Objem nákladového prostoru jedné dodávky v m³.</param>
/// <param name="VanWeightKg">Maximální povolená hmotnost nákladu jedné dodávky v kg.</param>
public sealed record FleetCapacity(int VanCount, double VanVolumeM3, double VanWeightKg)
{
    /// <summary>Zadání: 120 dodávek, 7 m³ a 5,5 t na dodávku.</summary>
    public static FleetCapacity Default { get; } = new(VanCount: 120, VanVolumeM3: 7.0, VanWeightKg: 5_500.0);

    /// <summary>Objemová kapacita celé flotily na jeden okruh (840 m³).</summary>
    public double TotalVolumeM3 => VanCount * VanVolumeM3;

    /// <summary>Hmotnostní kapacita celé flotily na jeden okruh (660 000 kg).</summary>
    public double TotalWeightKg => VanCount * VanWeightKg;

    /// <summary>
    /// Hustota, při které se úzké hrdlo překlápí z objemu na hmotnost (~785,7 kg/m³).
    /// Reálné zásilky mívají 100–250 kg/m³, takže obvykle limituje objem – algoritmus na to ale nespoléhá.
    /// </summary>
    public double BreakEvenDensityKgPerM3 => TotalWeightKg / TotalVolumeM3;

    /// <summary>Vejde se zásilka vůbec do jedné dodávky?</summary>
    public bool IsTransportable(in Package package)
        => package.VolumeM3 <= VanVolumeM3 && package.WeightKg <= VanWeightKg;
}
