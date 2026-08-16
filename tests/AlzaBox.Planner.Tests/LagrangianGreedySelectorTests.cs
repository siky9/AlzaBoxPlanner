using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Selection;

namespace AlzaBox.Planner.Tests;

public class LagrangianGreedySelectorTests
{
    private readonly LagrangianGreedySelector _selector = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Vyber_se_drzi_blizko_optima_spocteneho_hrubou_silou(int seed)
    {
        Package[] packages = TestBatches.Random(count: 18, seed: seed);
        FleetCapacity capacity = TestBatches.TinyFleet;

        SelectionResult result = _selector.Select(packages, capacity);
        double optimum = TestBatches.BruteForceOptimum(packages, capacity);

        Assert.True(result.RevenueCzk <= optimum + 1e-9,
            $"Hladový výběr {result.RevenueCzk:F2} nemůže překonat optimum {optimum:F2}.");

        // Malá dávka je pro hladový výběr nejhorší případ – jedna zásilka je tu velká část
        // kapacity. I tak se čeká, že se optimu výrazně přiblíží.
        Assert.True(result.RevenueCzk >= 0.85 * optimum,
            $"Hladový výběr {result.RevenueCzk:F2} je příliš daleko od optima {optimum:F2}.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Horni_mez_nikdy_nepodstreli_skutecne_optimum(int seed)
    {
        Package[] packages = TestBatches.Random(count: 18, seed: seed);
        FleetCapacity capacity = TestBatches.TinyFleet;

        SelectionResult result = _selector.Select(packages, capacity);
        double optimum = TestBatches.BruteForceOptimum(packages, capacity);

        Assert.True(result.UpperBoundCzk >= optimum - 1e-6,
            $"Horní mez {result.UpperBoundCzk:F2} je pod optimem {optimum:F2}, takže neplatí.");
    }

    [Fact]
    public void Kdyz_se_vejde_vsechno_vezme_se_vsechno_a_mez_je_tesna()
    {
        // Úterý a čtvrtek – nabídka se do flotily vejde celá (~595 m³ z 840 m³).
        Package[] packages = TestBatches.Random(count: 3_000, seed: 7, minDensity: 60, maxDensity: 200);

        SelectionResult result = _selector.Select(packages, FleetCapacity.Default);

        Assert.Equal(packages.Length, result.SelectedIndices.Length);
        Assert.Equal(packages.Sum(package => package.RevenueCzk), result.RevenueCzk, 3);
        Assert.Equal(result.RevenueCzk, result.UpperBoundCzk, 3);
        Assert.Equal(0, result.GreedyRuns); // triviální případ se pozná bez jediného řazení
    }

    [Fact]
    public void Zasilky_vetsi_nez_dodavka_se_nikdy_nevyberou()
    {
        FleetCapacity capacity = FleetCapacity.Default;
        Package[] packages =
        [
            new(Id: 1, WeightKg: 10, VolumeM3: 0.5, RevenueCzk: 1_000),
            new(Id: 2, WeightKg: 10, VolumeM3: 9.0, RevenueCzk: 9_999_999),   // objemem přes dodávku
            new(Id: 3, WeightKg: 6_000, VolumeM3: 0.5, RevenueCzk: 9_999_999) // hmotností přes dodávku
        ];

        SelectionResult result = _selector.Select(packages, capacity);

        Assert.Equal([0], result.SelectedIndices);
        Assert.Equal(1_000, result.RevenueCzk);
    }

    [Fact]
    public void Kdyz_limituje_jen_objem_stavi_hladovy_vyber_na_hustote_vynosu()
    {
        // Lehké zboží – nosnost se nemůže stát bottleneckem, θ musí zůstat na 1.
        Package[] packages = TestBatches.Random(count: 50_000, seed: 11, minDensity: 30, maxDensity: 120);

        SelectionResult result = _selector.Select(packages, FleetCapacity.Default);

        Assert.Equal(1.0, result.Theta);
        Assert.Equal(1, result.GreedyRuns); // jediný průchod stačí
        Assert.True(result.WeightKg < FleetCapacity.Default.TotalWeightKg);
        Assert.True(result.GapPercent < 0.01, $"Odstup od meze {result.GapPercent:F4} % je příliš velký.");
    }

    [Fact]
    public void Kdyz_limituji_obe_omezeni_vyjde_theta_dovnitr_intervalu()
    {
        // Široký rozptyl hustot kolem zlomu 786 kg/m³ – zásilky se dají namíchat tak,
        // aby došel objem i nosnost naráz, ani jedno omezení proto nestačí samo o sobě.
        Package[] packages = TestBatches.Random(count: 50_000, seed: 13, minDensity: 300, maxDensity: 1_600);
        FleetCapacity capacity = FleetCapacity.Default;

        SelectionResult result = _selector.Select(packages, capacity);

        double theta = Assert.NotNull(result.Theta);
        Assert.InRange(theta, 0.001, 0.999);
        Assert.True(result.VolumeM3 > 0.99 * capacity.TotalVolumeM3);
        Assert.True(result.WeightKg > 0.99 * capacity.TotalWeightKg);
        Assert.True(result.GapPercent < 0.1, $"Odstup od meze {result.GapPercent:F4} % je příliš velký.");
    }

    [Fact]
    public void Prazdna_davka_nespadne()
    {
        SelectionResult result = _selector.Select([], FleetCapacity.Default);

        Assert.Empty(result.SelectedIndices);
        Assert.Equal(0, result.RevenueCzk);
    }

    [Fact]
    public void Vysledek_je_deterministicky()
    {
        Package[] packages = TestBatches.Random(count: 30_000, seed: 21, minDensity: 400, maxDensity: 1_200);

        SelectionResult first = _selector.Select(packages, FleetCapacity.Default);
        SelectionResult second = new LagrangianGreedySelector().Select(packages, FleetCapacity.Default);

        Assert.Equal(first.RevenueCzk, second.RevenueCzk);
        Assert.Equal(first.SelectedIndices, second.SelectedIndices);
    }
}
