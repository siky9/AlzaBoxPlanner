using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Cli;

/// <summary>Profil nabídky zásilek – mění, které omezení bude pro flotilu bottleneckem.</summary>
public enum BatchProfile
{
    /// <summary>Běžný mix e-shopu. Průměrná hustota hluboko pod 786 kg/m³, limituje objem.</summary>
    Mixed,

    /// <summary>Vysoký podíl těžkého zboží (baterie, nářadí, nápoje). Limituje hmotnost.</summary>
    Heavy,

    /// <summary>Objemné lehké zboží. Objem dojde s velkou rezervou v nosnosti.</summary>
    Light,

    /// <summary>
    /// Kusové zboží velikosti nábytku či bílé techniky – 0,6–3 m³ a k tomu 2 % kusů přes 7 m³,
    /// které se do dodávky nevejdou vůbec. Zásilka tu přestává být proti dodávce drobná, takže
    /// se rozpad na výběr a nakládání začne lámat o zrnitost – mezní případ předpokladu, že je
    /// zásilka proti dodávce drobná. Je na něm vidět verdikt <c>GranularityLimited</c>
    /// i hlášení o nepřepravitelném zboží.
    /// </summary>
    Bulky,
}

/// <summary>
/// Generátor syntetické nabídky zásilek. Rozdělení jsou zvolena tak, aby velikostně
/// odpovídala běžnému e-shopovému mixu – jde o testovací data, ne o model reality.
/// </summary>
public static class PackageGenerator
{
    public static Package[] Generate(int count, BatchProfile profile, int seed)
    {
        var random = new Random(seed);
        var packages = new Package[count];

        for (int i = 0; i < count; i++)
        {
            double volume = NextVolume(random, profile);
            double density = NextDensity(random, profile);
            double weight = Math.Round(volume * density, 3);

            // Výnos roste s hodnotou zboží, ta jen volně souvisí s velikostí – proto silný šum.
            double revenue = Math.Round(60.0 + 900.0 * Math.Pow(volume, 0.35) * Math.Exp(NextNormal(random, 0, 0.8)), 2);

            packages[i] = new Package(Id: i + 1, WeightKg: weight, VolumeM3: volume, RevenueCzk: revenue);
        }

        return packages;
    }

    private static double NextVolume(Random random, BatchProfile profile) => profile switch
    {
        // Kusové zboží: nejmenší zásilka je desetina dodávky, největší skoro půlka.
        // Pár procent je rovnou přes dodávku – sedačka nebo velká lednička se do 7 m³ nevejde
        // a potřebuje jiné vozidlo. Na dávce je tak vidět i hlášení o nepřepravitelném zboží.
        BatchProfile.Bulky => random.NextDouble() < 0.02
            ? Math.Round(7.5 + random.NextDouble() * 4.0, 5)
            : Math.Round(0.6 + random.NextDouble() * 2.4, 5),

        // Jinak log-normálně: hodně malých zásilek, dlouhý chvost velkých. ~0,001–1,5 m³.
        _ => Math.Clamp(Math.Round(Math.Exp(NextNormal(random, mean: -3.9, deviation: 0.9)), 5), 0.0005, 1.5),
    };

    private static double NextDensity(Random random, BatchProfile profile) => profile switch
    {
        // 10 % velmi hustého zboží, zbytek běžný mix.
        BatchProfile.Mixed => random.NextDouble() < 0.10
            ? random.NextDouble() * 700 + 600
            : random.NextDouble() * 200 + 80,
        BatchProfile.Heavy => random.NextDouble() < 0.55
            ? random.NextDouble() * 900 + 900
            : random.NextDouble() * 300 + 150,
        BatchProfile.Light => random.NextDouble() * 120 + 30,
        // Nábytek a bílá technika – lehké na svůj objem, nosnost se bottleneckem nestane.
        BatchProfile.Bulky => random.NextDouble() * 140 + 60,
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    /// <summary>Box–Muller: normální rozdělení ze dvou uniformních vzorků.</summary>
    private static double NextNormal(Random random, double mean, double deviation)
    {
        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return mean + deviation * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
