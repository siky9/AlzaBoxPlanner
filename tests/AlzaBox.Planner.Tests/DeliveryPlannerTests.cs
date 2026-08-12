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
}
