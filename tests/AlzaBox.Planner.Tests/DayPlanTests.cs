using AlzaBox.Planner.Core;
using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Validation;

namespace AlzaBox.Planner.Tests;

/// <summary>
/// Zadání říká, že dodávky objedou okruh dvakrát denně. Druhá jízda plánuje nad zhuštěným
/// zbytkem a indexy se přepisují zpět na globální – tyhle testy hlídají hlavně to.
/// </summary>
public class DayPlanTests
{
    private readonly DeliveryPlanner _planner = new();

    [Fact]
    public void Druha_jizda_veze_prave_to_co_prvni_nechala()
    {
        Package[] packages = TestBatches.Random(count: 200_000, seed: 31, minDensity: 80, maxDensity: 300);

        DayPlan day = _planner.PlanDay(packages, FleetCapacity.Default, rounds: 2);

        Assert.Empty(LoadPlanValidator.Validate(packages, day));
        Assert.Equal(2, day.Rounds.Count);

        // Nabídka druhého okruhu je přesně zbytek po prvním a nic z ní nechybí ani nepřebývá.
        Assert.Equal(packages.Length - day.Rounds[0].LoadedPackageCount, day.Rounds[0].UnloadedIndices.Length);
        Assert.Equal(packages.Length, day.LoadedPackageCount + day.UndeliveredIndices.Length);
    }

    [Fact]
    public void Zasilka_se_nikdy_neveze_dvakrat_za_den()
    {
        Package[] packages = TestBatches.Random(count: 150_000, seed: 33, minDensity: 300, maxDensity: 1_600);

        DayPlan day = _planner.PlanDay(packages, FleetCapacity.Default, rounds: 3);

        var seen = new HashSet<int>();
        foreach (LoadPlan round in day.Rounds)
        {
            foreach (Van van in round.Vans)
            {
                foreach (int index in van.PackageIndices)
                    Assert.True(seen.Add(index), $"Zásilka {index} je naložena ve dvou okruzích.");
            }
        }

        Assert.Equal(day.LoadedPackageCount, seen.Count);
        Assert.Empty(LoadPlanValidator.Validate(packages, day));
    }

    [Fact]
    public void Den_odveze_vic_nez_jediny_okruh()
    {
        Package[] packages = TestBatches.Random(count: 200_000, seed: 35, minDensity: 80, maxDensity: 300);

        LoadPlan single = _planner.Plan(packages, FleetCapacity.Default);
        DayPlan day = _planner.PlanDay(packages, FleetCapacity.Default, rounds: 2);

        Assert.True(day.RevenueCzk > single.RevenueCzk,
            $"Druhá jízda nic nepřidala: den {day.RevenueCzk:F0} Kč, jeden okruh {single.RevenueCzk:F0} Kč.");

        // První okruh dne musí být tentýž plán jako samostatný okruh – druhá jízda ho neovlivňuje.
        Assert.Equal(single.RevenueCzk, day.Rounds[0].RevenueCzk);
    }

    [Fact]
    public void Kdyz_sklad_dojde_dalsi_okruh_uz_nevyjede()
    {
        // Úterý a čtvrtek: nabídka se vejde do prvního okruhu, druhá jízda nemá co vézt.
        Package[] packages = TestBatches.Random(count: 3_000, seed: 37, minDensity: 60, maxDensity: 200);

        DayPlan day = _planner.PlanDay(packages, FleetCapacity.Default, rounds: 2);

        Assert.Single(day.Rounds);
        Assert.Empty(day.UndeliveredIndices);
        Assert.Equal(packages.Length, day.LoadedPackageCount);
    }

    [Fact]
    public void Nabidka_okruhu_muze_byt_libovolna_podmnozina()
    {
        // Mezi jízdami se doskladňuje: nabídka další jízdy = zbytek + novinky. Tenhle test
        // ověřuje ten obecný vstup – plán nad podmnožinou drží indexy do celého skladu.
        Package[] packages = TestBatches.Random(count: 100_000, seed: 39, minDensity: 80, maxDensity: 400);
        int[] offer = [.. Enumerable.Range(0, packages.Length).Where(index => index % 3 == 0)];

        LoadPlan plan = _planner.Plan(packages, offer, FleetCapacity.Default);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));

        var allowed = offer.ToHashSet();
        foreach (Van van in plan.Vans)
        {
            foreach (int index in van.PackageIndices)
                Assert.Contains(index, allowed);
        }

        Assert.Equal(offer.Length, plan.LoadedPackageCount + plan.UnloadedIndices.Length);
    }

    [Fact]
    public void Jeden_okruh_dne_je_totozny_s_primym_planovanim()
    {
        Package[] packages = TestBatches.Random(count: 50_000, seed: 41, minDensity: 200, maxDensity: 900);

        LoadPlan direct = _planner.Plan(packages, FleetCapacity.Default);
        DayPlan day = _planner.PlanDay(packages, FleetCapacity.Default, rounds: 1);

        Assert.Single(day.Rounds);
        Assert.Equal(direct.RevenueCzk, day.RevenueCzk);
        Assert.Equal(direct.LoadedPackageCount, day.LoadedPackageCount);
    }

    [Fact]
    public void Okruh_ktery_nic_nenalozi_den_ukonci()
    {
        // Sklad je samé nadměrné zboží. Nabídka se nikdy nevyprázdní, takže dřív se plánovalo
        // všech pět okruhů naprázdno – jeden stačí, další by dopadl úplně stejně.
        var packages = new Package[5_000];
        for (int i = 0; i < packages.Length; i++)
            packages[i] = new Package(Id: i + 1, WeightKg: 10, VolumeM3: 8.0, RevenueCzk: 1_000);

        DayPlan day = _planner.PlanDay(packages, FleetCapacity.Default, rounds: 5);

        Assert.Single(day.Rounds);
        Assert.Equal(0, day.LoadedPackageCount);
        Assert.Equal(packages.Length, day.UndeliveredIndices.Length);
        Assert.Equal(packages.Length, day.NonTransportableCount);
    }

    [Fact]
    public void Nulovy_pocet_okruhu_je_chyba()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => _planner.PlanDay(TestBatches.Random(count: 100, seed: 43), FleetCapacity.Default, rounds: 0));
}
