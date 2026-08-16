using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Selection;

namespace AlzaBox.Planner.Cli;

/// <summary>
/// Srovnávací strategie – hladový výběr podle jednoduchého, pevně daného kritéria.
/// Prochází stejnou nakládací fází jako ostrý plánovač, takže se výsledky dají poctivě porovnat.
/// </summary>
public sealed class PrioritySelector(string name, Func<Package, double> priority) : IPackageSelector
{
    public string Name => name;

    /// <summary>Sada strategií, se kterými se v reportu měříme.</summary>
    public static IReadOnlyList<PrioritySelector> Baselines { get; } =
    [
        new("Nejdražší zásilky první", package => package.RevenueCzk),
        new("Nejvyšší výnos na m³", package => package.RevenueCzk / Math.Max(package.VolumeM3, 1e-9)),
        new("Nejvyšší výnos na kg", package => package.RevenueCzk / Math.Max(package.WeightKg, 1e-9)),
    ];

    public SelectionResult Select(ReadOnlySpan<Package> packages, FleetCapacity capacity)
    {
        int count = packages.Length;
        var key = new double[count];
        var order = new int[count];
        double transportableRevenue = 0;

        for (int i = 0; i < count; i++)
        {
            order[i] = i;
            bool transportable = capacity.IsTransportable(packages[i]);
            key[i] = transportable ? -priority(packages[i]) : double.PositiveInfinity;
            if (transportable) transportableRevenue += packages[i].RevenueCzk;
        }

        Array.Sort(key, order);

        double volumeLeft = capacity.TotalVolumeM3;
        double weightLeft = capacity.TotalWeightKg;
        double revenue = 0;
        var selected = new List<int>();

        for (int rank = 0; rank < count; rank++)
        {
            if (double.IsPositiveInfinity(key[rank])) break;

            int index = order[rank];
            ref readonly Package package = ref packages[index];
            if (package.VolumeM3 > volumeLeft || package.WeightKg > weightLeft) continue;

            selected.Add(index);
            volumeLeft -= package.VolumeM3;
            weightLeft -= package.WeightKg;
            revenue += package.RevenueCzk;
        }

        return new SelectionResult
        {
            SelectedIndices = [.. selected],
            RankedOrder = order,
            RevenueCzk = revenue,
            VolumeM3 = capacity.TotalVolumeM3 - volumeLeft,
            WeightKg = capacity.TotalWeightKg - weightLeft,
            // Srovnávací strategie Lagrangeovu mez nepočítá. Místo nekonečna (které by z každého
            // odstupu udělalo NaN) bereme triviálně platnou mez: víc než všechno se naložit nedá.
            UpperBoundCzk = transportableRevenue,
            Theta = null, // stínovou cenu tahle strategie nehledá
            GreedyRuns = 1,
        };
    }
}
