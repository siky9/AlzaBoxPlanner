using AlzaBox.Planner.Core.Assignment;
using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Selection;

namespace AlzaBox.Planner.Core;

/// <summary>
/// Naplánuje jeden okruh flotily: vybere nejvýnosnější zásilky a rozdělí je do dodávek.
/// </summary>
/// <remarks>
/// Dvě fáze úmyslně: <see cref="IPackageSelector"/> řeší <i>co</i> se poveze (tam jsou peníze),
/// <see cref="VanAssigner"/> řeší <i>čím</i> se to poveze (tam je jen proveditelnost).
/// Obojí lze vyměnit nezávisle.
/// </remarks>
public sealed class DeliveryPlanner(IPackageSelector selector, VanAssigner assigner)
{
    public DeliveryPlanner() : this(new LagrangianGreedySelector(), new VanAssigner())
    {
    }

    public LoadPlan Plan(ReadOnlySpan<Package> packages, FleetCapacity capacity)
    {
        SelectionResult selection = selector.Select(packages, capacity);
        return assigner.Assign(packages, selection, capacity);
    }

    public LoadPlan Plan(ReadOnlySpan<Package> packages) => Plan(packages, FleetCapacity.Default);

    /// <summary>
    /// Naplánuje jeden okruh nad <b>podmnožinou</b> skladu – <paramref name="offer"/> jsou indexy
    /// zásilek, které jsou pro tento okruh k dispozici.
    /// </summary>
    /// <remarks>
    /// Indexy ve výsledném plánu ukazují do <paramref name="packages"/>, ne do nabídky, takže se
    /// plány z různých okruhů dají skládat a porovnávat. Optimalizuje se ale jen nad nabídkou:
    /// řadí se <c>offer.Length</c> položek, ne celý sklad.
    /// </remarks>
    public LoadPlan Plan(ReadOnlySpan<Package> packages, ReadOnlySpan<int> offer, FleetCapacity capacity)
    {
        var batch = new Package[offer.Length];
        for (int i = 0; i < offer.Length; i++) batch[i] = packages[offer[i]];

        SelectionResult selection = selector.Select(batch, capacity);

        return assigner.Assign(packages, ToWarehouseIndices(selection, offer), capacity);
    }

    /// <summary>
    /// Naplánuje celý den: flotila objede okruh <paramref name="rounds"/>krát a každá další
    /// jízda dostane to, co předchozí neodvezly. Zadání počítá se dvěma jízdami denně.
    /// </summary>
    /// <remarks>
    /// Okruhy se plánují nezávisle a hladově za sebou – druhá jízda nemůže první nic vzít.
    /// Když se mezi jízdami stihne naskladnit nové zboží, patří do nabídky druhé jízdy:
    /// stačí zavolat <see cref="Plan(ReadOnlySpan{Package}, ReadOnlySpan{int}, FleetCapacity)"/>
    /// s <see cref="LoadPlan.UnloadedIndices"/> doplněnými o indexy novinek.
    /// </remarks>
    public DayPlan PlanDay(ReadOnlySpan<Package> packages, FleetCapacity capacity, int rounds = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);

        var plans = new List<LoadPlan>(rounds);
        int[] offer = [];

        for (int round = 0; round < rounds; round++)
        {
            LoadPlan plan = round == 0 ? Plan(packages, capacity) : Plan(packages, offer, capacity);

            plans.Add(plan);
            offer = plan.UnloadedIndices;

            // Další okruh má smysl, jen když je co vozit. Prázdný sklad je jasný případ;
            // stejně tak ale okruh, který nenaložil nic – to znamená, že ze zbytku flotila
            // neuveze vůbec nic (typicky samé nadměrné zboží) a další jízda by dopadla stejně.
            if (offer.Length == 0 || plan.LoadedPackageCount == 0) break;
        }

        return new DayPlan { Capacity = capacity, Rounds = plans, UndeliveredIndices = offer };
    }

    /// <summary>
    /// Přepíše indexy z nabídky okruhu zpět na indexy do celého skladu. Výběrová fáze pracuje
    /// nad zhuštěným polem, nakládací fáze už dostane globální indexy.
    /// </summary>
    private static SelectionResult ToWarehouseIndices(SelectionResult selection, ReadOnlySpan<int> offer)
    {
        var selected = new int[selection.SelectedIndices.Length];
        for (int i = 0; i < selected.Length; i++) selected[i] = offer[selection.SelectedIndices[i]];

        var ranked = new int[selection.RankedOrder.Length];
        for (int i = 0; i < ranked.Length; i++) ranked[i] = offer[selection.RankedOrder[i]];

        return new SelectionResult
        {
            SelectedIndices = selected,
            RankedOrder = ranked,
            RevenueCzk = selection.RevenueCzk,
            VolumeM3 = selection.VolumeM3,
            WeightKg = selection.WeightKg,
            UpperBoundCzk = selection.UpperBoundCzk,
            Theta = selection.Theta,
            GreedyRuns = selection.GreedyRuns,
        };
    }
}
