using AlzaBox.Planner.Core;
using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Validation;

namespace AlzaBox.Planner.Tests;

public class DeliveryPlannerTests
{
    private readonly DeliveryPlanner _planner = new();

    [Theory]
    [InlineData(60, 200)]      // lehké zboží – limituje objem
    [InlineData(600, 1_400)]   // těžké zboží – limituje nosnost i objem
    [InlineData(80, 1_500)]    // široký mix
    public void Plan_je_vzdy_proveditelny(double minDensity, double maxDensity)
    {
        Package[] packages = TestBatches.Random(count: 40_000, seed: 3, minDensity: minDensity, maxDensity: maxDensity);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
    }

    [Fact]
    public void Rozdeleni_do_dodavek_uzke_hrdlo_temer_nezmrha()
    {
        // Nejtěžší případ pro rozdělování: obě omezení jsou blízko sebe, takže dodávka
        // musí dostat správný mix hustot, jinak se zablokuje v jednom rozměru.
        Package[] packages = TestBatches.Random(count: 200_000, seed: 5, minDensity: 300, maxDensity: 1_600);
        FleetCapacity capacity = FleetCapacity.Default;

        LoadPlan plan = _planner.Plan(packages, capacity);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
        Assert.True(plan.GapPercent < 0.5,
            $"Odstup od horní meze {plan.GapPercent:F3} % je příliš velký.");
        Assert.True(Math.Max(plan.VolumeUtilization, plan.WeightUtilization) > 0.99,
            $"Úzké hrdlo zůstalo nevyužité: objem {plan.VolumeUtilization:P2}, nosnost {plan.WeightUtilization:P2}.");
    }

    [Fact]
    public void Kdyz_se_vejde_vsechno_neni_nic_vynechano()
    {
        Package[] packages = TestBatches.Random(count: 3_000, seed: 9, minDensity: 60, maxDensity: 200);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
        Assert.Equal(packages.Length, plan.LoadedPackageCount);
        Assert.Equal(packages.Sum(package => package.RevenueCzk), plan.RevenueCzk, 3);
    }

    [Fact]
    public void Zadna_dodavka_neprekroci_kapacitu()
    {
        Package[] packages = TestBatches.Random(count: 50_000, seed: 17, minDensity: 500, maxDensity: 1_200);
        FleetCapacity capacity = FleetCapacity.Default;

        LoadPlan plan = _planner.Plan(packages, capacity);

        foreach (Van van in plan.Vans)
        {
            Assert.True(van.RemainingVolumeM3 >= -1e-9, $"Dodávka {van.Index} má záporný zbytek objemu.");
            Assert.True(van.RemainingWeightKg >= -1e-9, $"Dodávka {van.Index} má záporný zbytek nosnosti.");
        }

        Assert.Equal(capacity.VanCount, plan.Vans.Count);
    }

    [Fact]
    public void Vynos_planu_nikdy_neprekroci_horni_mez()
    {
        Package[] packages = TestBatches.Random(count: 100_000, seed: 23, minDensity: 200, maxDensity: 1_800);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.True(plan.RevenueCzk <= plan.Selection.UpperBoundCzk + 1e-6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(3_000)]
    public void Kdyz_se_vejde_vsechno_vyjede_jen_nutny_pocet_dodavek(int count)
    {
        // Úterý a čtvrtek: posílat 120 poloprázdných dodávek by bylo dražší, ne rychlejší.
        Package[] packages = TestBatches.Random(count, seed: 42, minDensity: 80, maxDensity: 300);
        FleetCapacity capacity = FleetCapacity.Default;

        LoadPlan plan = _planner.Plan(packages, capacity);

        int minimumVans = Math.Max(
            (int)Math.Ceiling(plan.VolumeM3 / capacity.VanVolumeM3),
            (int)Math.Ceiling(plan.WeightKg / capacity.VanWeightKg));

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
        Assert.Equal(packages.Length, plan.LoadedPackageCount);
        Assert.Equal(minimumVans, plan.UsedVanCount);
    }

    [Fact]
    public void Kdyz_je_nabidka_vetsi_nez_flotila_vyjedou_vsechny_dodavky()
    {
        // Konsolidace nesmí zadržet kapacitu, když je pro ni využití.
        Package[] packages = TestBatches.Random(count: 100_000, seed: 29, minDensity: 80, maxDensity: 300);
        FleetCapacity capacity = FleetCapacity.Default;

        LoadPlan plan = _planner.Plan(packages, capacity);

        Assert.Equal(capacity.VanCount, plan.UsedVanCount);
        Assert.True(plan.VolumeUtilization > 0.999, $"Objem využit jen na {plan.VolumeUtilization:P2}.");
    }

    [Fact]
    public void Zasilka_velka_skoro_jako_dodavka_dostane_dodavku_pro_sebe()
    {
        // Mezní případ předpokladu „zásilka je proti dodávce drobná“: dvě zásilky po 3,6 m³
        // se do jedné dodávky nevejdou, takže flotila uveze právě 120 kusů. Souhrnná
        // kapacita jich připouští 233 – horní mez je tu proto volná, ne plán špatný.
        var packages = new Package[400];
        for (int i = 0; i < packages.Length; i++)
            packages[i] = new Package(Id: i + 1, WeightKg: 100, VolumeM3: 3.6, RevenueCzk: 1_000);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
        Assert.Equal(FleetCapacity.Default.VanCount, plan.LoadedPackageCount);
        Assert.All(plan.Vans, van => Assert.Single(van.PackageIndices));
    }

    [Fact]
    public void Odstup_od_optima_nevychazi_zaporne()
    {
        // Když plán horní meze dosáhne, liší se oba součty jen pořadím sčítání –
        // hlášený odstup přesto nesmí spadnout pod nulu (dřív se tiskl „-0 Kč“).
        for (int count = 1; count <= 400; count += 37)
        {
            LoadPlan plan = _planner.Plan(TestBatches.Random(count, seed: count), FleetCapacity.Default);

            Assert.True(plan.GapCzk >= 0.0, $"Odstup {plan.GapCzk} Kč vyšel záporný.");
            Assert.True(plan.GapPercent >= 0.0, $"Odstup {plan.GapPercent} % vyšel záporný.");
            Assert.Equal(0.0, plan.GapPercent, 9); // veze se všechno, odstup je nulový až na zaokrouhlení
        }
    }
}
