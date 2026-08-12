using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Core.Selection;

/// <summary>
/// Výběrová fáze plánování: z nabídky zásilek vybere podmnožinu s maximální výnosností,
/// která se vejde do <b>souhrnné</b> kapacity flotily (objem i hmotnost).
/// </summary>
public interface IPackageSelector
{
    SelectionResult Select(ReadOnlySpan<Package> packages, FleetCapacity capacity);
}
