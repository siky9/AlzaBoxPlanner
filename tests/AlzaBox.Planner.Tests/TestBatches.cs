using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Tests;

internal static class TestBatches
{
    /// <summary>Náhodná dávka s volitelným rozsahem hustoty – pro testy proveditelnosti.</summary>
    public static Package[] Random(int count, int seed, double minDensity = 80, double maxDensity = 1500)
    {
        var random = new Random(seed);
        var packages = new Package[count];

        for (int i = 0; i < count; i++)
        {
            double volume = Math.Round(0.001 + random.NextDouble() * 0.4, 5);
            double density = minDensity + random.NextDouble() * (maxDensity - minDensity);
            packages[i] = new Package(
                Id: i + 1,
                WeightKg: Math.Round(volume * density, 3),
                VolumeM3: volume,
                RevenueCzk: Math.Round(50 + random.NextDouble() * 3000, 2));
        }

        return packages;
    }

    /// <summary>Malá flotila, aby se dala úloha vyřešit hrubou silou.</summary>
    public static FleetCapacity TinyFleet => new(VanCount: 2, VanVolumeM3: 1.0, VanWeightKg: 300.0);

    /// <summary>
    /// Optimum souhrnné úlohy (dvourozměrný batoh) hrubou silou přes všechny podmnožiny.
    /// Použitelné jen pro velmi malé dávky – slouží jako referenční hodnota v testech.
    /// </summary>
    public static double BruteForceOptimum(Package[] packages, FleetCapacity capacity)
    {
        if (packages.Length > 22) throw new ArgumentException("Hrubá síla zvládne nejvýš 22 zásilek.");

        double best = 0;

        for (long mask = 0; mask < 1L << packages.Length; mask++)
        {
            double volume = 0, weight = 0, revenue = 0;

            for (int i = 0; i < packages.Length; i++)
            {
                if ((mask & (1L << i)) == 0) continue;
                if (!capacity.IsTransportable(packages[i])) { revenue = -1; break; }

                volume += packages[i].VolumeM3;
                weight += packages[i].WeightKg;
                revenue += packages[i].RevenueCzk;
            }

            if (revenue > best && volume <= capacity.TotalVolumeM3 && weight <= capacity.TotalWeightKg)
                best = revenue;
        }

        return best;
    }
}
