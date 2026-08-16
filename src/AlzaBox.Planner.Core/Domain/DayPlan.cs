namespace AlzaBox.Planner.Core.Domain;

/// <summary>
/// Plán celého dne – flotila objede okruh několikrát za sebou (podle zadání dvakrát denně).
/// </summary>
/// <remarks>
/// Každý okruh je samostatné plánování nad tím, co zbylo z předchozích jízd. Jednotlivé
/// <see cref="LoadPlan"/>y odkazují indexy do stejného pole zásilek, takže se dají skládat
/// a žádná zásilka se nemůže objevit ve dvou okruzích naráz.
/// </remarks>
public sealed class DayPlan
{
    public required FleetCapacity Capacity { get; init; }

    /// <summary>Okruhy v pořadí, ve kterém se jedou.</summary>
    public required IReadOnlyList<LoadPlan> Rounds { get; init; }

    /// <summary>
    /// Zásilky, které neodvezl ani poslední okruh – nabídka pro zítřek (plus to, co se do té
    /// doby naskladní).
    /// </summary>
    public required int[] UndeliveredIndices { get; init; }

    public double RevenueCzk => Rounds.Sum(round => round.RevenueCzk);

    public int LoadedPackageCount => Rounds.Sum(round => round.LoadedPackageCount);

    /// <summary>Kolik jízd dodávek den celkem stojí – součet přes okruhy, ne počet vozidel.</summary>
    public int VanTripCount => Rounds.Sum(round => round.UsedVanCount);

    /// <summary>
    /// Součet odstupů jednotlivých okruhů: kolik korun ztratil <b>výběr</b>, měřeno v každém
    /// okruhu proti jeho vlastní nabídce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Není to certifikát proti optimu celého dne, a nevydávám ho za něj. Jednotlivé okruhy
    /// mají svoje meze poctivé, ale jejich součet nemá stejně čistý důkaz jako mez pro jeden
    /// okruh: nabídky nejsou disjunktní, nýbrž vnořené (druhý okruh vybírá ze zbytku prvního),
    /// zatímco optimální rozvržení dne se naší volbou prvního okruhu vázat nemusí. Přímočará
    /// úvaha dá jen <c>mez₁ + 2·mez₂</c>.
    /// </para>
    /// <para>
    /// Empiricky mez drží: na 2 000 malých instancích (dva i tři okruhy, pět různých rozdělení
    /// včetně samých velkých kusů a extrémního rozptylu výnosů) proti optimu spočtenému hrubou
    /// silou nepadla ani jednou a v nejtěsnějším případě vyšla přesně na optimum. Berte to
    /// tedy jako ověřené, ne dokázané – a hlavně jako součet ztrát po okruzích, což je otázka,
    /// na kterou plánovač opravdu odpovídá.
    /// </para>
    /// </remarks>
    public double GapCzk => Rounds.Sum(round => round.GapCzk);

    /// <summary>Zásilky, které neuveze žádná dodávka – napříč okruhy jsou to pořád tytéž kusy.</summary>
    public int NonTransportableCount => Rounds.Count > 0 ? Rounds[^1].NonTransportableCount : 0;

    /// <summary>Výnos ležící ladem v nepřepravitelných zásilkách.</summary>
    public double NonTransportableRevenueCzk => Rounds.Count > 0 ? Rounds[^1].NonTransportableRevenueCzk : 0;
}
