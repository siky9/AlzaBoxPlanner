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
        Row("Naloženo zásilek", $"{plan.LoadedPackageCount:N0}  ({100.0 * plan.LoadedPackageCount / packages.Length:N1} % nabídky)");
        Row("Stínová cena θ", $"{plan.Selection.Theta:N4}   ({DescribeTheta(plan.Selection.Theta)})");
        Row("Hladových průchodů", $"{plan.Selection.GreedyRuns}");
        Row("Využití objemu", Bar(plan.VolumeUtilization));
        Row("Využití nosnosti", Bar(plan.WeightUtilization));
        Row("Použito dodávek", $"{plan.UsedVanCount} / {capacity.VanCount}");
        Row("Dosypáno / nevešlo se", $"{plan.TopUpCount:N0} / {plan.UnplacedFromSelectionCount:N0}");

        Section("Výnosnost");
        Row("Výnos plánu", $"{plan.RevenueCzk:N0} Kč");
        Row("Horní mez optima", $"{plan.Selection.UpperBoundCzk:N0} Kč");
        Row("Odstup od optima", $"{plan.GapPercent:N4} %  (nejvýš {plan.GapCzk:N0} Kč)");

        if (baselines.Count > 0)
        {
            Section("Srovnání s naivními strategiemi (stejná nakládací fáze)");
            foreach ((string name, double revenue) in baselines) CompareRow(name, revenue, plan.RevenueCzk);
        }

        Section("Čas");
        Row("Výběr zásilek", $"{selectionTime.TotalMilliseconds:N1} ms");
        Row("Rozdělení do dodávek", $"{assignmentTime.TotalMilliseconds:N1} ms");
        Row("Celkem", $"{(selectionTime + assignmentTime).TotalMilliseconds:N1} ms");
        Console.WriteLine();
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
        double delta = 100.0 * (ours - baseline) / baseline;
        string verdict = delta switch
        {
            > 0.005 => $"náš plán je o {delta,0:N2} % lepší",
            < -0.005 => $"náš plán je o {-delta,0:N2} % horší",
            _ => "shodně s naším plánem",
        };
        Console.WriteLine($"  {label,-28} {baseline,14:N0} Kč   →  {verdict}");
    }

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

    private static string DescribeTheta(double theta) => theta switch
    {
        >= 0.999 => "úzkým hrdlem je objem",
        <= 0.001 => "úzkým hrdlem je hmotnost",
        _ => "obě omezení jsou aktivní",
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
