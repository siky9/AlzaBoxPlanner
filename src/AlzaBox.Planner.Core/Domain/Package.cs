namespace AlzaBox.Planner.Core.Domain;

/// <summary>
/// Jedna zásilka připravená k odvozu do AlzaBoxu.
/// </summary>
/// <remarks>
/// Konkrétní rozměry neřešíme – objem bereme jako aditivní veličinu, žádné 3D balení.
/// Typ je <c>readonly record struct</c> záměrně: pole stovek tisíc struktur se v paměti chová
/// sekvenčně, nezatěžuje GC a dobře se z něj čte při lineárních průchodech.
/// </remarks>
/// <param name="Id">Identifikátor zásilky (WMS / číslo balíku).</param>
/// <param name="WeightKg">Hmotnost v kilogramech.</param>
/// <param name="VolumeM3">Objem v m³.</param>
/// <param name="RevenueCzk">
/// Výnosnost v Kč. Předpokládáme kladnou – zásilku se zápornou by hladový výběr sice zařadil
/// až nakonec, ale kdyby zbyla kapacita, naložil by ji a výnos by si tím snížil.
/// </param>
public readonly record struct Package(int Id, double WeightKg, double VolumeM3, double RevenueCzk)
{
    /// <summary>Hustota zásilky v kg/m³. Rozhoduje o tom, které omezení bude pro flotilu bottleneckem.</summary>
    public double DensityKgPerM3 => VolumeM3 > 0 ? WeightKg / VolumeM3 : double.PositiveInfinity;
}
