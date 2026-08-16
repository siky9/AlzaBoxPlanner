using AlzaBox.Planner.Core;
using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Validation;

namespace AlzaBox.Planner.Tests;

/// <summary>
/// Nakládací fáze. Na reálném mixu drobných zásilek dosypávání nikdy nezasáhne – flotila je
/// po hlavním průchodu plná na setiny m³. Aby ta fáze nebyla nepokrytá, je tu dávka
/// s hrubou zrnitostí, kde zasáhnout musí.
/// </summary>
public class VanAssignerTests
{
    private readonly DeliveryPlanner _planner = new();

    /// <summary>Deset dodávek místo 120, ať se dá výsledek spočítat na papíře.</summary>
    private static FleetCapacity SmallFleet => new(VanCount: 10, VanVolumeM3: 7.0, VanWeightKg: 5_500.0);

    /// <summary>
    /// Dávka, ve které hlavní průchod nechá v každé dodávce díru, kterou umí zaplnit jen
    /// zásilky, jež výběr zamítl:
    /// <list type="bullet">
    /// <item>25 kusů po 2,5 m³ za 1 000 Kč/m³ – do dodávky se vejdou dva a 2 m³ zůstanou volné,</item>
    /// <item>300 kusů po 0,05 m³ za 800 Kč/m³ – horší výnosnost, takže jdou na řadu až po nich.</item>
    /// </list>
    /// Nabídka je 77,5 m³ proti 70 m³ kapacity, takže výběr musí část drobných odmítnout –
    /// a přesně ty pak dosypávání vrací do hry.
    /// </summary>
    private static Package[] CoarseGrainedBatch()
    {
        var packages = new Package[325];

        for (int i = 0; i < 25; i++)
            packages[i] = new Package(Id: i + 1, WeightKg: 250, VolumeM3: 2.5, RevenueCzk: 2_500);

        for (int i = 25; i < packages.Length; i++)
            packages[i] = new Package(Id: i + 1, WeightKg: 5, VolumeM3: 0.05, RevenueCzk: 40);

        return packages;
    }

    [Fact]
    public void Dosypavani_zaplni_diry_po_velkych_zasilkach()
    {
        Package[] packages = CoarseGrainedBatch();

        LoadPlan plan = _planner.Plan(packages, SmallFleet);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));

        // Hlavní průchod naloží 20 velkých (2 na dodávku) a 5 mu jich zbude – do 2 m³ díry
        // se třetí nevejde. Dosypávání pak do těch děr dostane všechny odmítnuté drobné.
        Assert.Equal(5, plan.UnplacedFromSelectionCount);
        Assert.True(plan.TopUpCount > 0, "Dosypávací fáze nezasáhla, přestože ve flotile zůstalo místo.");
        Assert.Equal(150, plan.TopUpCount);

        // Výsledek: 20 velkých + úplně všechny drobné.
        Assert.Equal(20 + 300, plan.LoadedPackageCount);
        Assert.Equal(20 * 2_500 + 300 * 40, plan.RevenueCzk);
    }

    [Fact]
    public void Dosypavani_pridava_jen_to_co_se_opravdu_vejde()
    {
        Package[] packages = CoarseGrainedBatch();
        FleetCapacity capacity = SmallFleet;

        LoadPlan plan = _planner.Plan(packages, capacity);

        // Dosypávání smí sáhnout jen do zbytkové kapacity – žádná dodávka se nesmí přeplnit
        // a pět neumístěných velkých zásilek musí zůstat venku (2,5 m³ se do 2m³ díry nevejde).
        foreach (Van van in plan.Vans)
        {
            Assert.True(van.RemainingVolumeM3 >= -1e-9);
            Assert.True(van.RemainingWeightKg >= -1e-9);
        }

        Assert.Equal(5, plan.UnloadedIndices.Length);
        Assert.All(plan.UnloadedIndices, index => Assert.Equal(2.5, packages[index].VolumeM3));
    }

    [Fact]
    public void Nasycena_flotila_se_pozna_a_mlci()
    {
        // Reálný mix: úzké hrdlo je vyčerpané, odstup od meze je skutečná ztráta.
        Package[] packages = TestBatches.Random(count: 200_000, seed: 51, minDensity: 80, maxDensity: 300);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Equal(CapacityVerdict.Saturated, plan.Verdict);
        Assert.True(plan.GapPercent < 0.1);
    }

    [Fact]
    public void Vycerpana_nabidka_se_nehlasi_jako_nedovyuzita_flotila()
    {
        // Úterý a čtvrtek: flotila skončí na 72 % objemu, ale není ji čím naplnit –
        // to není nedostatek plánu a nesmí se hlásit jako promarněné místo.
        Package[] packages = TestBatches.Random(count: 3_000, seed: 53, minDensity: 60, maxDensity: 200);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Equal(CapacityVerdict.NothingLeftToCarry, plan.Verdict);
        Assert.Equal(packages.Length, plan.LoadedPackageCount);
        Assert.True(plan.VolumeUtilization < 0.999, "Test má smysl jen na nedoplněné flotile.");
    }

    [Fact]
    public void Nadmerne_zbozi_se_nesmi_ztratit_potichu()
    {
        // Půl skladu je přes 7 m³ a je v něm drtivá většina peněz. Výběr takové zásilky
        // odfiltruje a horní mez s nimi nepočítá, takže by plán vyšel jako „hotovo, odstup 0 %“.
        var packages = new Package[1_000];
        for (int i = 0; i < 500; i++)
            packages[i] = new Package(Id: i + 1, WeightKg: 10, VolumeM3: 0.05, RevenueCzk: 100);
        for (int i = 500; i < packages.Length; i++)
            packages[i] = new Package(Id: i + 1, WeightKg: 10, VolumeM3: 9.0, RevenueCzk: 100_000);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
        Assert.Equal(500, plan.LoadedPackageCount);

        // Odstup od optima je poctivě nula – mez počítá jen s tím, co flotila uveze.
        // Právě proto musí být nadměrné zboží vidět jinudy.
        Assert.Equal(0.0, plan.GapPercent, 9);
        Assert.Equal(500, plan.NonTransportableCount);
        Assert.Equal(500 * 100_000.0, plan.NonTransportableRevenueCzk);
    }

    [Fact]
    public void Na_beznem_mixu_zadne_nadmerne_zbozi_neni()
    {
        Package[] packages = TestBatches.Random(count: 50_000, seed: 55, minDensity: 80, maxDensity: 300);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Equal(0, plan.NonTransportableCount);
        Assert.Equal(0.0, plan.NonTransportableRevenueCzk);
    }

    [Fact]
    public void Hruba_zrnitost_se_pozna_a_upozorni_ze_mez_je_volna()
    {
        // 400 zásilek po 3,6 m³: do dodávky se vejde jediná, takže flotila uveze 120 kusů
        // a stojí na 51 % objemu. Plán je optimální – volná je horní mez, ne řešení.
        var packages = new Package[400];
        for (int i = 0; i < packages.Length; i++)
            packages[i] = new Package(Id: i + 1, WeightKg: 100, VolumeM3: 3.6, RevenueCzk: 1_000);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
        Assert.Equal(CapacityVerdict.GranularityLimited, plan.Verdict);

        // Přesně ten případ, kvůli kterému verdikt existuje: nízké využití, velký odstup,
        // a přesto se nedá naložit víc.
        Assert.True(plan.VolumeUtilization < 0.6);
        Assert.True(plan.GapPercent > 10);
        Assert.Equal(FleetCapacity.Default.VanCount, plan.LoadedPackageCount);
    }

    [Fact]
    public void Nizke_vyuziti_samo_o_sobe_verdikt_neurcuje()
    {
        // Dvě dávky se skoro shodným využitím objemu, ale jiným důvodem – verdikt je musí
        // rozlišit, jinak by šlo o pouhý práh na procentech.
        var coarse = new Package[400];
        for (int i = 0; i < coarse.Length; i++)
            coarse[i] = new Package(Id: i + 1, WeightKg: 100, VolumeM3: 3.6, RevenueCzk: 1_000);

        var exhausted = new Package[120];
        for (int i = 0; i < exhausted.Length; i++)
            exhausted[i] = new Package(Id: i + 1, WeightKg: 100, VolumeM3: 3.6, RevenueCzk: 1_000);

        LoadPlan coarsePlan = _planner.Plan(coarse, FleetCapacity.Default);
        LoadPlan exhaustedPlan = _planner.Plan(exhausted, FleetCapacity.Default);

        Assert.Equal(coarsePlan.VolumeUtilization, exhaustedPlan.VolumeUtilization, 9);
        Assert.Equal(CapacityVerdict.GranularityLimited, coarsePlan.Verdict);
        Assert.Equal(CapacityVerdict.NothingLeftToCarry, exhaustedPlan.Verdict);
    }

    [Fact]
    public void Na_drobnem_mixu_uz_dosypavani_nema_co_delat()
    {
        // Protipól k testům výš a doklad tvrzení z README: u statisíců drobných zásilek
        // zaplní hlavní průchod flotilu tak těsně, že dosypávání nezbyde použitelná mezera.
        Package[] packages = TestBatches.Random(count: 200_000, seed: 47, minDensity: 80, maxDensity: 300);

        LoadPlan plan = _planner.Plan(packages, FleetCapacity.Default);

        Assert.Empty(LoadPlanValidator.Validate(packages, plan));
        Assert.Equal(0, plan.TopUpCount);
        Assert.True(plan.VolumeUtilization > 0.999, $"Objem využit jen na {plan.VolumeUtilization:P4}.");
    }
}
