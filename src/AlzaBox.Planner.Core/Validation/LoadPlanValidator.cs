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

    /// <summary>
    /// Kontrola celodenního plánu: každý okruh sám o sobě, a navíc že se okruhy nepřekrývají.
    /// Druhá jízda plánuje nad zhuštěnou nabídkou a indexy se přepisují zpět na globální –
    /// právě tam by se chyba schovala nejsnáz, takže se ověřuje adresně.
    /// </summary>
    public static IReadOnlyList<string> Validate(ReadOnlySpan<Package> packages, DayPlan day)
    {
        var problems = new List<string>();
        var loaded = new bool[packages.Length];

        foreach (LoadPlan round in day.Rounds)
        {
            problems.AddRange(Validate(packages, round));

            foreach (Van van in round.Vans)
            {
                foreach (int index in van.PackageIndices)
                {
                    if (index < 0 || index >= packages.Length) continue; // ohlásil už průchod okruhem
                    if (loaded[index]) problems.Add($"Zásilka na indexu {index} je naložena ve dvou okruzích.");
                    loaded[index] = true;
                }
            }
        }

        foreach (int index in day.UndeliveredIndices)
        {
            if (index < 0 || index >= packages.Length)
                problems.Add($"Nedoručená zásilka: index {index} je mimo rozsah.");
            else if (loaded[index])
                problems.Add($"Zásilka na indexu {index} je hlášena jako nedoručená, ale někdo ji veze.");
        }

        return problems;
    }

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

        // Po dosypávací fázi se nesmí stát, že ve flotile zbylo místo i zásilky do něj.
        if (plan.Verdict == CapacityVerdict.SpaceLeftUnused)
            problems.Add("Ve flotile zůstalo použitelné místo i zásilky, které by se do něj vešly.");

        problems.AddRange(ValidateLeftovers(plan, seen, packages.Length));

        return problems;
    }

    /// <summary>
    /// Kontrola zbytku ve skladu. <c>UnloadedIndices</c> není jen údaj do reportu – je to
    /// nabídka dalšího okruhu, takže chyba v něm by zásilku buď ztratila, nebo poslala na cestu
    /// dvakrát. Nabídku tohohle okruhu poznáme z <c>RankedOrder</c>, což je celá seřazená nabídka.
    /// </summary>
    private static IReadOnlyList<string> ValidateLeftovers(LoadPlan plan, bool[] loaded, int packageCount)
    {
        var problems = new List<string>();
        var listed = new bool[packageCount];

        foreach (int index in plan.UnloadedIndices)
        {
            if (index < 0 || index >= packageCount)
            {
                problems.Add($"Zbytek ve skladu: index {index} je mimo rozsah.");
                continue;
            }

            if (loaded[index]) problems.Add($"Zásilka na indexu {index} je naložená, ale hlásí se jako zbylá.");
            if (listed[index]) problems.Add($"Zásilka na indexu {index} je ve zbytku uvedena dvakrát.");
            listed[index] = true;
        }

        int offered = plan.Selection.RankedOrder.Length;
        if (plan.LoadedPackageCount + plan.UnloadedIndices.Length != offered)
        {
            problems.Add($"Nabídka měla {offered} zásilek, ale naloženo {plan.LoadedPackageCount} " +
                         $"a ve skladu zbylo {plan.UnloadedIndices.Length} – některá se ztratila.");
        }

        return problems;
    }
}
