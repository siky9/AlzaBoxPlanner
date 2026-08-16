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

    /// <summary>
    /// Co z nabídky zůstalo ve skladu – indexy zásilek, které tento okruh neodvezl.
    /// Přesně tohle jede příště: podle zadání dodávky objedou okruh dvakrát denně, takže
    /// je to vstup druhé jízdy (viz <see cref="DayPlan"/>).
    /// </summary>
    /// <remarks>
    /// Zahrnuje jak zásilky, které výběr zamítl, tak ty, které se do dodávek nevešly, i ty
    /// nepřepravitelné (&gt; 7 m³ nebo &gt; 5,5 t) – ty potřebují jiné vozidlo, ne další okruh.
    /// Pořadí je sestupné podle výhodnosti při vítězném θ.
    /// </remarks>
    public required int[] UnloadedIndices { get; init; }

    /// <summary>
    /// O co se plán zastavil. Rozhoduje o tom, jestli je <see cref="GapPercent"/> údaj o ztrátě,
    /// nebo jen o volnosti horní meze.
    /// </summary>
    public required CapacityVerdict Verdict { get; init; }

    /// <summary>
    /// Kolik zásilek z nabídky neuveze <b>žádná</b> dodávka téhle flotily (&gt; 7 m³ nebo &gt; 5,5 t).
    /// </summary>
    /// <remarks>
    /// Nejsou to zásilky čekající na další okruh – ty potřebují jiné vozidlo a příští jízda
    /// s nimi nepohne. Hlásí se zvlášť právě proto, že se jinak z úlohy vytratí bez hlásky:
    /// výběr je odfiltruje a horní mez s nimi nepočítá, takže sklad plný nadměrného zboží
    /// jinak vypadá jako splněný plán s nulovým odstupem od optima.
    /// </remarks>
    public required int NonTransportableCount { get; init; }

    /// <summary>Výnos, který v nepřepravitelných zásilkách leží ladem.</summary>
    public required double NonTransportableRevenueCzk { get; init; }

    public double VolumeUtilization => VolumeM3 / Capacity.TotalVolumeM3;

    public double WeightUtilization => WeightKg / Capacity.TotalWeightKg;

    public int UsedVanCount => Vans.Count(van => van.PackageIndices.Count > 0);

    /// <summary>Kolik korun nám nejvýš uniklo proti teoretickému optimu.</summary>
    /// <remarks>
    /// Ořezáno zdola nulou: když se plán s horní mezí potká, liší se oba součty stejných
    /// <c>double</c>ů jen pořadím sčítání a rozdíl může vyjít nepatrně záporný.
    /// <para>
    /// Je to <b>horní</b> odhad ztráty. Jak je těsný, říká <see cref="Verdict"/>: u nasycené
    /// flotily je to prakticky přesná ztráta, u <see cref="CapacityVerdict.GranularityLimited"/>
    /// může být plán optimální a odstup přesto velký.
    /// </para>
    /// </remarks>
    public double GapCzk => Math.Max(0.0, Selection.UpperBoundCzk - RevenueCzk);

    /// <summary>Kolik procent výnosnosti nám nejvýš uniklo proti teoretickému optimu.</summary>
    public double GapPercent => Selection.UpperBoundCzk > 0
        ? 100.0 * GapCzk / Selection.UpperBoundCzk
        : 0.0;
}
