namespace AlzaBox.Planner.Core.Selection;

/// <summary>
/// Výsledek výběrové fáze – které zásilky se vyplatí naložit do flotily jako celku.
/// </summary>
public sealed class SelectionResult
{
    /// <summary>Indexy vybraných zásilek do vstupního pole.</summary>
    public required int[] SelectedIndices { get; init; }

    /// <summary>
    /// Všechny zásilky seřazené sestupně podle výhodnosti při vítězném θ.
    /// Používá se v dosypávací fázi (<see cref="Assignment.VanAssigner"/>), aby se zbytková
    /// kapacita dodávek doplňovala od nejvýnosnějších zásilek a nemuselo se znovu řadit.
    /// </summary>
    public required int[] RankedOrder { get; init; }

    public required double RevenueCzk { get; init; }

    public required double VolumeM3 { get; init; }

    public required double WeightKg { get; init; }

    /// <summary>
    /// Horní odhad dosažitelné výnosnosti (Lagrangeova relaxace). Rozdíl proti
    /// <see cref="RevenueCzk"/> je certifikát kvality řešení – kolik Kč nám nejvýš uniklo.
    /// </summary>
    public required double UpperBoundCzk { get; init; }

    /// <summary>
    /// Váha objemu v cenové funkci; 1 = rozhoduje jen objem, 0 = rozhoduje jen hmotnost.
    /// <c>null</c> u strategií, které stínovou cenu vůbec nehledají – lepší než ji předstírat
    /// hodnotou, kterou by pak někdo bral vážně.
    /// </summary>
    public required double? Theta { get; init; }

    /// <summary>Kolik průchodů (řazení) si hledání θ vyžádalo.</summary>
    public required int GreedyRuns { get; init; }

    /// <summary>
    /// Odchylka od horní meze v procentech. Ořezáno zdola nulou – když výběr meze dosáhne,
    /// liší se oba součty stejných <c>double</c>ů jen pořadím sčítání.
    /// </summary>
    public double GapPercent => UpperBoundCzk > 0
        ? Math.Max(0.0, 100.0 * (UpperBoundCzk - RevenueCzk) / UpperBoundCzk)
        : 0.0;
}
