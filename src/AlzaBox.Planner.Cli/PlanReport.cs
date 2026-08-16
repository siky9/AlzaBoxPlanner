using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Cli;

/// <summary>Textový výstup jednoho plánování.</summary>
public static class PlanReport
{
    public static void Print(ReadOnlySpan<Package> packages, LoadPlan plan,
                             TimeSpan selectionTime, TimeSpan assignmentTime,
                             IReadOnlyList<(string Name, double RevenueCzk)> baselines)
    {
        FleetCapacity capacity = plan.Capacity;

        Section("Vstup");
        Row("Zásilek v nabídce", $"{packages.Length:N0}");
        Row("Průměrná hustota", $"{AverageDensity(packages):N0} kg/m³   (zlom objem/hmotnost: " +
                                $"{capacity.BreakEvenDensityKgPerM3:N0} kg/m³)");
        Row("Kapacita flotily", $"{capacity.VanCount} × {capacity.VanVolumeM3:N0} m³ / " +
                                $"{capacity.VanWeightKg / 1000:N1} t = {capacity.TotalVolumeM3:N0} m³ / " +
                                $"{capacity.TotalWeightKg / 1000:N0} t");

        Section("Výběr");
        Row("Naloženo zásilek", $"{plan.LoadedPackageCount:N0}  ({Share(plan.LoadedPackageCount, packages.Length):N1} % nabídky)");
        Row("Stínová cena θ", DescribeTheta(plan.Selection.Theta));
        Row("Hladových průchodů", $"{plan.Selection.GreedyRuns}");
        Row("Využití objemu", Bar(plan.VolumeUtilization));
        Row("Využití nosnosti", Bar(plan.WeightUtilization));
        Row("Použito dodávek", $"{plan.UsedVanCount} / {capacity.VanCount}");
        Row("Dosypáno / nevešlo se", $"{plan.TopUpCount:N0} / {plan.UnplacedFromSelectionCount:N0}");

        Section("Výnosnost");
        Row("Výnos plánu", $"{plan.RevenueCzk:N0} Kč");
        Row("Horní mez optima", $"{plan.Selection.UpperBoundCzk:N0} Kč");
        Row("Odstup od optima", $"{plan.GapPercent:N4} %  (nejvýš {plan.GapCzk:N0} Kč)");
        PrintNonTransportable(plan.NonTransportableCount, plan.NonTransportableRevenueCzk, capacity);
        PrintVerdict(plan);

        if (baselines.Count > 0)
        {
            Section("Srovnání s naivními strategiemi (stejná nakládací fáze)");
            foreach ((string name, double revenue) in baselines) CompareRow(name, revenue, plan.RevenueCzk);
        }

        Section("Čas");
        Row("Výběr zásilek", $"{selectionTime.TotalMilliseconds:N1} ms");
        Row("Rozdělení do dodávek", $"{assignmentTime.TotalMilliseconds:N1} ms");
        Row("Celkem za plánování", $"{(selectionTime + assignmentTime).TotalMilliseconds:N1} ms");
        if (baselines.Count > 0) Row("", "(bez srovnávacích strategií – ty se počítají navíc)");
        Console.WriteLine();
    }

    /// <summary>
    /// Výstup celodenního plánu – jeden řádek na okruh plus součty. Zajímavé je, jak druhá
    /// jízda vypadá proti první: pokud je sklad hluboký, veze skoro totéž; když dochází,
    /// vyjede míň dodávek a výnos spadne.
    /// </summary>
    public static void PrintDay(ReadOnlySpan<Package> packages, DayPlan day, TimeSpan elapsed)
    {
        FleetCapacity capacity = day.Capacity;

        Section("Vstup");
        Row("Zásilek v nabídce", $"{packages.Length:N0}");
        Row("Průměrná hustota", $"{AverageDensity(packages):N0} kg/m³   (zlom objem/hmotnost: " +
                                $"{capacity.BreakEvenDensityKgPerM3:N0} kg/m³)");
        Row("Kapacita flotily", $"{capacity.VanCount} × {capacity.VanVolumeM3:N0} m³ / " +
                                $"{capacity.VanWeightKg / 1000:N1} t na okruh, {day.Rounds.Count}× denně");

        Section($"Okruhy dne ({day.Rounds.Count})");
        Console.WriteLine("  okruh   zásilek    objem    nosnost   dodávek        výnos Kč     odstup");

        for (int i = 0; i < day.Rounds.Count; i++)
        {
            LoadPlan round = day.Rounds[i];
            Console.WriteLine($"  {i + 1,5}   {round.LoadedPackageCount,7:N0}   " +
                              $"{round.VolumeUtilization,6:P1}   {round.WeightUtilization,7:P1}   " +
                              $"{round.UsedVanCount,3} / {capacity.VanCount,-3}   " +
                              $"{round.RevenueCzk,13:N0}   {round.GapPercent,7:N4} %");
        }

        for (int i = 0; i < day.Rounds.Count; i++) PrintVerdict(day.Rounds[i], $"Okruh {i + 1}: ");

        Section("Den celkem");
        Row("Doručeno zásilek", $"{day.LoadedPackageCount:N0}  ({Share(day.LoadedPackageCount, packages.Length):N1} % nabídky)");
        Row("Zbývá ve skladu", $"{day.UndeliveredIndices.Length:N0} zásilek za {RevenueOf(packages, day.UndeliveredIndices):N0} Kč");
        Row("Jízd dodávek", $"{day.VanTripCount:N0}  (strop {capacity.VanCount * day.Rounds.Count:N0})");
        Row("Výnos dne", $"{day.RevenueCzk:N0} Kč");
        Row("Ztráta výběru", $"nejvýš {day.GapCzk:N0} Kč (součet odstupů po okruzích)");
        PrintNonTransportable(day.NonTransportableCount, day.NonTransportableRevenueCzk, capacity);

        Section("Čas");
        Row("Naplánování dne", $"{elapsed.TotalMilliseconds:N1} ms");
        Console.WriteLine();
    }

    /// <summary>
    /// Upozornění, když se odstup od optima nedá číst jako ztráta. U nasycené flotily
    /// a u vyčerpané nabídky se nevypisuje nic – tam je odstup přímo tím, čím se tváří.
    /// </summary>
    /// <summary>
    /// Nadměrné zboží se z úlohy vytrácí potichu – výběr ho odfiltruje a horní mez s ním
    /// nepočítá, takže by odstup od optima vyšel nula i nad skladem plným takových zásilek.
    /// V celodenním plánu jsou to napříč okruhy pořád tytéž kusy, proto se hlásí jen jednou.
    /// </summary>
    private static void PrintNonTransportable(int count, double revenueCzk, FleetCapacity capacity)
    {
        if (count == 0) return;

        Console.WriteLine($"  ⚠ {count:N0} zásilek za {revenueCzk:N0} Kč neuveze žádná dodávka " +
                          $"(> {capacity.VanVolumeM3:N0} m³ nebo > {capacity.VanWeightKg / 1000:N1} t).");
        Console.WriteLine("    Nečekají na další okruh, potřebují jiné vozidlo – a do odstupu od optima");
        Console.WriteLine("    se nepromítají, protože mez počítá jen s tím, co flotila fyzicky uveze.");
    }

    private static void PrintVerdict(LoadPlan plan, string prefix = "")
    {
        double bottleneck = Math.Max(plan.VolumeUtilization, plan.WeightUtilization);

        switch (plan.Verdict)
        {
            case CapacityVerdict.GranularityLimited:
                Console.WriteLine($"  ⚠ {prefix}Bottleneck zůstal na {bottleneck:P1} a přesto se nedá naložit víc:");
                Console.WriteLine("    zbylé zásilky jsou tak velké, že se do žádné mezery nevejdou. Horní mez bere");
                Console.WriteLine("    kapacitu jako tekutinu, takže odstup výš ztrátu nadhodnocuje.");
                break;

            case CapacityVerdict.SpaceLeftUnused:
                Console.WriteLine($"  ⚠ {prefix}Ve flotile zůstalo místo i zásilky, které by se do něj vešly –");
                Console.WriteLine("    to je chyba nakládací fáze, ne vlastnost dávky.");
                break;
        }
    }

    private static double RevenueOf(ReadOnlySpan<Package> packages, int[] indices)
    {
        double total = 0;
        foreach (int index in indices) total += packages[index].RevenueCzk;
        return total;
    }

    public static void PrintVanDetail(LoadPlan plan, int limit)
    {
        Section($"Náplň dodávek (prvních {limit})");
        Console.WriteLine("   #   zásilek     objem m³ (%)        hmotnost kg (%)          výnos Kč");

        foreach (Van van in plan.Vans.Take(limit))
        {
            double volume = plan.Capacity.VanVolumeM3 - van.RemainingVolumeM3;
            double weight = plan.Capacity.VanWeightKg - van.RemainingWeightKg;
            Console.WriteLine($" {van.Index,3}   {van.PackageIndices.Count,7:N0}   " +
                              $"{volume,7:N3} ({van.VolumeUtilization,6:P1})   " +
                              $"{weight,9:N1} ({van.WeightUtilization,6:P1})   " +
                              $"{van.RevenueCzk,12:N0}");
        }

        Console.WriteLine();
    }

    private static void CompareRow(string label, double baseline, double ours)
    {
        // Prázdná dávka dá nulu na obou stranách – dělit by znamenalo vypsat NaN.
        string verdict = baseline <= 0
            ? "není s čím porovnat"
            : (100.0 * (ours - baseline) / baseline) switch
            {
                > 0.005 and var delta => $"náš plán je o {delta,0:N2} % lepší",
                < -0.005 and var delta => $"náš plán je o {-delta,0:N2} % horší",
                _ => "shodně s naším plánem",
            };

        Console.WriteLine($"  {label,-28} {baseline,14:N0} Kč   →  {verdict}");
    }

    /// <summary>Podíl v procentech; prázdná nabídka dá nulu místo NaN.</summary>
    private static double Share(double part, double whole) => whole > 0 ? 100.0 * part / whole : 0.0;

    private static double AverageDensity(ReadOnlySpan<Package> packages)
    {
        double volume = 0, weight = 0;
        foreach (ref readonly Package package in packages)
        {
            volume += package.VolumeM3;
            weight += package.WeightKg;
        }

        return volume > 0 ? weight / volume : 0;
    }

    private static string DescribeTheta(double? theta) => theta switch
    {
        null => "neurčeno   (strategie stínovou cenu nehledá)",
        >= 0.999 => $"{theta:N4}   (bottleneckem je objem)",
        <= 0.001 => $"{theta:N4}   (bottleneckem je hmotnost)",
        _ => $"{theta:N4}   (obě omezení jsou aktivní)",
    };

    private static string Bar(double ratio)
    {
        const int width = 24;
        int filled = (int)Math.Round(Math.Clamp(ratio, 0, 1) * width);
        return $"[{new string('█', filled)}{new string('·', width - filled)}] {ratio,7:P2}";
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('─', 78));
    }

    private static void Row(string label, string value)
        => Console.WriteLine($"  {label,-28} {value}");
}
