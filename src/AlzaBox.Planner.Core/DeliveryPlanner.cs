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
}
