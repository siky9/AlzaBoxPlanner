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

        for (int i = 0; i < count; i++)
        {
            order[i] = i;
            key[i] = capacity.IsTransportable(packages[i]) ? -priority(packages[i]) : double.PositiveInfinity;
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
            UpperBoundCzk = double.PositiveInfinity, // srovnávací strategie horní mez nepočítá
            Theta = double.NaN,
            GreedyRuns = 1,
        };
    }
}
