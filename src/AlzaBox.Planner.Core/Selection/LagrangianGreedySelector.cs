using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Core.Selection;

/// <summary>
/// Výběr zásilek pro celou flotilu jako <b>dvourozměrný batoh</b> (objem + hmotnost),
/// řešený hladově podle Lagrangeovy ceny obou zdrojů.
/// </summary>
/// <remarks>
/// <para>
/// Každá zásilka dostane skóre <c>výnos / cena</c>, kde
/// <c>cena(θ) = θ·objem/celkový_objem + (1−θ)·hmotnost/celková_hmotnost</c>.
/// Parametr θ je stínová cena zdrojů: říká, jestli je vzácnější místo, nebo nosnost.
/// Zásilky se pak berou sestupně podle skóre, dokud se do flotily vejdou.
/// </para>
/// <para>
/// Správné θ hledáme půlením intervalu tak, aby se oba zdroje vyčerpaly současně.
/// Nejčastěji ale stačí jediný průchod: pokud při θ=1 (rozhoduje jen objem) hmotnost
/// nikdy nikoho neodmítla, je hmotnostní omezení neaktivní a úloha je jednorozměrná –
/// tam je hladový výběr podle hustoty výnosu optimem LP relaxace.
/// </para>
/// <para>
/// Kvalita: LP relaxace batohu s <i>k</i> omezeními má v optimu nejvýš <i>k</i> zlomkové
/// položky. Pro k = 2 se tedy hladové řešení liší od optima relaxace nejvýš o dvě zásilky,
/// což je při statisících balíků zaokrouhlovací chyba. Skutečnou vzdálenost od optima
/// navíc vyčíslujeme (<see cref="SelectionResult.UpperBoundCzk"/>), takže se na ni nemusí věřit.
/// </para>
/// </remarks>
public sealed class LagrangianGreedySelector : IPackageSelector
{
    /// <summary>
    /// Strop na počet kroků půlení intervalu při hledání θ. 12 kroků = přesnost ~2·10⁻⁴;
    /// v praxi se skoro vždy skončí dřív, viz <see cref="RevenueTolerance"/>.
    /// </summary>
    private const int BisectionSteps = 12;

    /// <summary>
    /// Kdy hledání θ končí: jakmile je dosažený výnos takhle blízko horní meze (0,05 %),
    /// je zbytek prokazatelně nedosažitelný a další průchody by jen spálily časové okno.
    /// </summary>
    /// <remarks>
    /// Mez z Lagrangeovy relaxace platí pro celou úlohu, ne jen pro právě zkoušené θ – když se
    /// jí výběr dotkne na 0,05 %, nemůže žádné jiné θ přinést víc než těch 0,05 %. Certifikát
    /// kvality tak slouží i jako podmínka ukončení: neladíme počet kroků, ale přímo to, co nás
    /// zajímá.
    /// <para>
    /// Hodnota je zvolená tak, aby zkrácení bylo <b>zadarmo</b>: na těžké dávce padne 14 průchodů
    /// na 9 (tedy zhruba čtvrtina času výběru), přičemž vyjde θ i plán do koruny stejný jako při
    /// plném půlení. Volnější práh už stojí peníze – při 10⁻³ se skončí po 5 průchodech, ale výnos
    /// spadne o 20 tisíc Kč a odstup od meze vyskočí z 0,015 % na 0,065 %.
    /// </para>
    /// </remarks>
    private const double RevenueTolerance = 5e-4;

    public SelectionResult Select(ReadOnlySpan<Package> packages, FleetCapacity capacity)
    {
        BatchStatistics stats = BatchStatistics.Collect(packages, capacity);

        // Fáze 0 – úterý a čtvrtek: nabídka se vejde celá, není co optimalizovat.
        // Pokrývá i degenerované případy (prázdná dávka, samé nepřepravitelné zboží) –
        // tam jsou součty nulové, takže se nabídka „vejde“ a nemusí se kvůli tomu řadit.
        if (stats.TotalVolumeM3 <= capacity.TotalVolumeM3 && stats.TotalWeightKg <= capacity.TotalWeightKg)
        {
            return SelectEverything(packages, capacity, stats);
        }

        var current = new Workspace(packages.Length);
        var best = new Workspace(packages.Length);

        // Fáze 1a – rozhoduje jen objem. Pokud hmotnost nikoho neodmítla, je úloha jednorozměrná.
        GreedyOutcome outcome = RunGreedy(packages, capacity, theta: 1.0, stats, current);
        (current, best) = (best, current);
        GreedyOutcome bestOutcome = outcome;
        double upperBound = outcome.UpperBoundCzk;
        int runs = 1;

        if (outcome.WeightWasBinding)
        {
            // Fáze 1b – rozhoduje jen hmotnost.
            outcome = RunGreedy(packages, capacity, theta: 0.0, stats, current);
            runs++;
            upperBound = Math.Min(upperBound, outcome.UpperBoundCzk);
            if (outcome.RevenueCzk > bestOutcome.RevenueCzk)
            {
                (current, best) = (best, current);
                bestOutcome = outcome;
            }

            // Fáze 1c – obě omezení jsou aktivní. Půlením hledáme θ, při kterém se
            // oba zdroje vyčerpají současně; f(θ) = využití_objemu − využití_hmotnosti klesá.
            if (outcome.VolumeWasBinding && !IsCloseEnough(bestOutcome.RevenueCzk, upperBound))
            {
                double low = 0.0, high = 1.0;
                for (int step = 0; step < BisectionSteps; step++)
                {
                    double middle = 0.5 * (low + high);
                    outcome = RunGreedy(packages, capacity, middle, stats, current);
                    runs++;
                    upperBound = Math.Min(upperBound, outcome.UpperBoundCzk);

                    // Nejlepší nález si držíme nezávisle na půlení – nespoléháme na monotonii f.
                    if (outcome.RevenueCzk > bestOutcome.RevenueCzk)
                    {
                        (current, best) = (best, current);
                        bestOutcome = outcome;
                    }

                    // Certifikát kvality je zároveň podmínkou ukončení: co zbývá k mezi,
                    // už žádné θ nedožene.
                    if (IsCloseEnough(bestOutcome.RevenueCzk, upperBound)) break;

                    if (outcome.VolumeUtilization > outcome.WeightUtilization) low = middle;
                    else high = middle;
                }
            }
        }

        return new SelectionResult
        {
            SelectedIndices = best.Selected.AsSpan(0, bestOutcome.Count).ToArray(),
            RankedOrder = best.Order,
            RevenueCzk = bestOutcome.RevenueCzk,
            VolumeM3 = bestOutcome.VolumeM3,
            WeightKg = bestOutcome.WeightKg,
            UpperBoundCzk = upperBound,
            Theta = bestOutcome.Theta,
            GreedyRuns = runs,
        };
    }

    /// <summary>
    /// Je výběr tak blízko horní meze, že další hledání θ už nemá co získat?
    /// Mez je platná pro celou úlohu, takže odstup od ní shora omezuje i to, co by našlo
    /// libovolné jiné θ.
    /// </summary>
    private static bool IsCloseEnough(double revenueCzk, double upperBoundCzk)
        => revenueCzk >= upperBoundCzk * (1.0 - RevenueTolerance);

    /// <summary>
    /// Jeden hladový průchod pro dané θ: ohodnotí, seřadí a nabere zásilky do souhrnné kapacity.
    /// Složitost O(n log n), dominuje řazení.
    /// </summary>
    private static GreedyOutcome RunGreedy(
        ReadOnlySpan<Package> packages,
        FleetCapacity capacity,
        double theta,
        in BatchStatistics stats,
        Workspace workspace)
    {
        int count = packages.Length;
        double volumePrice = theta / capacity.TotalVolumeM3;
        double weightPrice = (1.0 - theta) / capacity.TotalWeightKg;

        double[] key = workspace.Key;
        int[] order = workspace.Order;
        int[] selected = workspace.Selected;

        // Klíč je záporné skóre, aby vzestupné Array.Sort dalo sestupné pořadí výhodnosti.
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
            ref readonly Package package = ref packages[i];

            if (!capacity.IsTransportable(package))
            {
                key[i] = double.PositiveInfinity; // nevejde se do žádné dodávky – řadíme úplně nakonec
                continue;
            }

            double cost = volumePrice * package.VolumeM3 + weightPrice * package.WeightKg;
            key[i] = cost > 0 ? -package.RevenueCzk / cost : double.NegativeInfinity;
        }

        Array.Sort(key, order);

        double volumeLeft = capacity.TotalVolumeM3;
        double weightLeft = capacity.TotalWeightKg;
        double revenue = 0.0;
        int taken = 0;
        int criticalRank = -1; // první odmítnutá zásilka určuje stínovou cenu kapacity

        for (int rank = 0; rank < count; rank++)
        {
            if (double.IsPositiveInfinity(key[rank])) break; // dál už jsou jen nepřepravitelné zásilky

            int index = order[rank];
            ref readonly Package package = ref packages[index];

            if (package.VolumeM3 <= volumeLeft && package.WeightKg <= weightLeft)
            {
                selected[taken++] = index;
                volumeLeft -= package.VolumeM3;
                weightLeft -= package.WeightKg;
                revenue += package.RevenueCzk;
            }
            else
            {
                // Degenerované zásilky s nulovou cenou (klíč −∞) by daly nekonečnou stínovou
                // cenu, na odhad horní meze bereme až první odmítnutou s konečným skóre.
                if (criticalRank < 0 && double.IsFinite(key[rank])) criticalRank = rank;

                // Zbytek kapacity nepojme ani tu nejmenší zásilku – dál nemá smysl procházet.
                if (volumeLeft < stats.MinVolumeM3 || weightLeft < stats.MinWeightKg) break;
            }
        }

        double criticalScore = criticalRank >= 0 ? -key[criticalRank] : 0.0;

        return new GreedyOutcome(
            Theta: theta,
            Count: taken,
            RevenueCzk: revenue,
            VolumeM3: capacity.TotalVolumeM3 - volumeLeft,
            WeightKg: capacity.TotalWeightKg - weightLeft,
            UpperBoundCzk: LagrangianBound.Evaluate(packages, capacity, theta, criticalScore),
            VolumeUtilization: 1.0 - volumeLeft / capacity.TotalVolumeM3,
            WeightUtilization: 1.0 - weightLeft / capacity.TotalWeightKg,
            // Kapacita zbylá na konci je zároveň nejmenší, jaká kdy byla. Když je větší než
            // největší zásilka, nemohla tímto zdrojem žádná zásilka propadnout.
            VolumeWasBinding: volumeLeft < stats.MaxVolumeM3,
            WeightWasBinding: weightLeft < stats.MaxWeightKg);
    }

    /// <summary>Triviální případ – veze se všechno, co je vůbec přepravitelné.</summary>
    private static SelectionResult SelectEverything(
        ReadOnlySpan<Package> packages, FleetCapacity capacity, in BatchStatistics stats)
    {
        var selected = new int[stats.TransportableCount];
        var order = new int[packages.Length];
        int taken = 0;

        for (int i = 0; i < packages.Length; i++)
        {
            order[i] = i;
            if (capacity.IsTransportable(packages[i])) selected[taken++] = i;
        }

        return new SelectionResult
        {
            SelectedIndices = selected,
            RankedOrder = order,
            RevenueCzk = stats.TotalRevenueCzk,
            VolumeM3 = stats.TotalVolumeM3,
            WeightKg = stats.TotalWeightKg,
            UpperBoundCzk = stats.TotalRevenueCzk, // nic víc naložit nejde, řešení je optimální
            Theta = 1.0,
            GreedyRuns = 0,
        };
    }

    /// <summary>Metriky jednoho hladového průchodu. Samotný výběr zůstává ve <see cref="Workspace"/>.</summary>
    private readonly record struct GreedyOutcome(
        double Theta,
        int Count,
        double RevenueCzk,
        double VolumeM3,
        double WeightKg,
        double UpperBoundCzk,
        double VolumeUtilization,
        double WeightUtilization,
        bool VolumeWasBinding,
        bool WeightWasBinding);

    /// <summary>
    /// Předalokovaná pracovní pole. Držíme dvě sady (aktuální + dosud nejlepší) a jen mezi nimi
    /// přehazujeme reference, takže hledání θ nealokuje ani nekopíruje.
    /// </summary>
    /// <remarks>
    /// Pole <see cref="Order"/> vítězné sady odchází ven jako <see cref="SelectionResult.RankedOrder"/>.
    /// Není to půjčka: <c>Workspace</c> po návratu z <c>Select</c> zaniká a nikdo jiný na to pole
    /// nedrží odkaz, takže ho volající dostává do vlastnictví – stejně jako <c>SelectedIndices</c>.
    /// </remarks>
    private sealed class Workspace(int capacity)
    {
        public double[] Key { get; } = new double[capacity];
        public int[] Order { get; } = new int[capacity];
        public int[] Selected { get; } = new int[capacity];
    }
}
