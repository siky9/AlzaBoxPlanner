using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Core.Validation;

/// <summary>
/// Kontrola, že nakládací plán je opravdu proveditelný. Používá se v testech i z CLI
/// přepínačem <c>--verify</c> – tvrzení o výnosnosti má cenu jen u plánu, který se dá naložit.
/// </summary>
public static class LoadPlanValidator
{
    /// <summary>Tolerance na akumulovanou chybu součtů v pohyblivé řádové čárce.</summary>
    private const double Epsilon = 1e-6;

    /// <summary>Vrátí seznam nalezených problémů; prázdný seznam znamená platný plán.</summary>
    public static IReadOnlyList<string> Validate(ReadOnlySpan<Package> packages, LoadPlan plan)
    {
        var problems = new List<string>();
        var seen = new bool[packages.Length];
        FleetCapacity capacity = plan.Capacity;

        if (plan.Vans.Count != capacity.VanCount)
            problems.Add($"Plán má {plan.Vans.Count} dodávek, očekáváno {capacity.VanCount}.");

        double totalRevenue = 0, totalVolume = 0, totalWeight = 0;
        int totalPackages = 0;

        foreach (Van van in plan.Vans)
        {
            double volume = 0, weight = 0, revenue = 0;

            foreach (int index in van.PackageIndices)
            {
                if (index < 0 || index >= packages.Length)
                {
                    problems.Add($"Dodávka {van.Index}: index zásilky {index} je mimo rozsah.");
                    continue;
                }

                if (seen[index]) problems.Add($"Zásilka na indexu {index} je naložena vícekrát.");
                seen[index] = true;

                ref readonly Package package = ref packages[index];
                volume += package.VolumeM3;
                weight += package.WeightKg;
                revenue += package.RevenueCzk;
            }

            if (volume > capacity.VanVolumeM3 + Epsilon)
                problems.Add($"Dodávka {van.Index}: objem {volume:F4} m³ přesahuje {capacity.VanVolumeM3} m³.");

            if (weight > capacity.VanWeightKg + Epsilon)
                problems.Add($"Dodávka {van.Index}: hmotnost {weight:F3} kg přesahuje {capacity.VanWeightKg} kg.");

            if (Math.Abs(revenue - van.RevenueCzk) > Math.Max(Epsilon, Math.Abs(revenue) * 1e-9))
                problems.Add($"Dodávka {van.Index}: evidovaný výnos nesouhlasí s obsahem.");

            totalRevenue += revenue;
            totalVolume += volume;
            totalWeight += weight;
            totalPackages += van.PackageIndices.Count;
        }

        if (totalPackages != plan.LoadedPackageCount)
            problems.Add($"Plán hlásí {plan.LoadedPackageCount} zásilek, v dodávkách jich je {totalPackages}.");

        if (Math.Abs(totalRevenue - plan.RevenueCzk) > Math.Max(Epsilon, Math.Abs(totalRevenue) * 1e-9))
            problems.Add("Celkový výnos plánu nesouhlasí se součtem dodávek.");

        if (totalVolume > capacity.TotalVolumeM3 + Epsilon)
            problems.Add("Celkový objem přesahuje kapacitu flotily.");

        if (totalWeight > capacity.TotalWeightKg + Epsilon)
            problems.Add("Celková hmotnost přesahuje nosnost flotily.");

        if (plan.RevenueCzk > plan.Selection.UpperBoundCzk * (1 + 1e-9))
            problems.Add("Výnos plánu překračuje horní mez – horní mez je spočtena špatně.");

        return problems;
    }
}
