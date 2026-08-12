using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Selection;

namespace AlzaBox.Planner.Core.Assignment;

/// <summary>
/// Rozdělení vybraných zásilek do konkrétních dodávek metodou Best-Fit Decreasing
/// a následné dosypání zbytkové kapacity.
/// </summary>
/// <remarks>
/// Výběrová fáze počítá se souhrnnou kapacitou flotily, což je relaxace – teoreticky se
/// vybraná množina nemusí do 120 dodávek rozdělit beze zbytku. Protože je ale zásilka
/// o tři až čtyři řády menší než dodávka, je ztráta zaokrouhlením v praxi nulová a to,
/// co přece jen propadne, nahradí dosypávací fáze jinou zásilkou srovnatelné hodnoty.
/// </remarks>
public sealed class VanAssigner
{
    public LoadPlan Assign(ReadOnlySpan<Package> packages, SelectionResult selection, FleetCapacity capacity)
    {
        var vans = new Van[capacity.VanCount];
        for (int i = 0; i < vans.Length; i++) vans[i] = new Van(i, capacity);

        var isPlaced = new bool[packages.Length];
        int[] toPlace = OrderByDecreasingSize(packages, selection.SelectedIndices, capacity);

        int placedCount = 0;
        foreach (int index in toPlace)
        {
            if (!TryPlace(vans, index, packages[index], capacity)) continue;
            isPlaced[index] = true;
            placedCount++;
        }

        int unplacedFromSelection = toPlace.Length - placedCount;
        int topUpCount = TopUp(packages, selection.RankedOrder, isPlaced, vans, capacity);

        return BuildPlan(vans, selection, capacity, placedCount + topUpCount, unplacedFromSelection, topUpCount);
    }

    /// <summary>
    /// Umístí zásilku do dodávky podle skalárního součinu poptávky a zbývající kapacity
    /// (heuristika „dot product“ pro vektorové bin-packing).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zásilka i dodávka se popíšou dvousložkovým vektorem v podílech kapacity jedné dodávky:
    /// zásilka <c>(objem/7, hmotnost/5500)</c>, dodávka svým zbytkem. Vybere se dodávka
    /// s největším skalárním součinem, tedy ta, které zbývá nejvíc právě toho, co zásilka žádá –
    /// těžké zboží putuje do dodávek s rezervou v nosnosti, objemné do dodávek s rezervou v místě.
    /// </para>
    /// <para>
    /// Klasický Best-Fit tu selhává: každou další zásilku cpe do nejplnější dodávky, takže se
    /// flotila rozdělí na dodávky vyčerpané na nosnost s volným místem a dodávky vyčerpané na
    /// objem s volnou nosností. Do takto polarizované flotily se pak nevejde nic. Skalární
    /// součin naopak drží náklad každé dodávky blízko hustotě, při které dojdou oba zdroje naráz.
    /// </para>
    /// </remarks>
    private static bool TryPlace(Van[] vans, int index, in Package package, FleetCapacity capacity)
    {
        double demandVolume = package.VolumeM3 / capacity.VanVolumeM3;
        double demandWeight = package.WeightKg / capacity.VanWeightKg;

        Van? bestVan = null;
        double bestScore = double.NegativeInfinity;

        foreach (Van van in vans)
        {
            if (!van.CanFit(package)) continue;

            double score = demandVolume * (van.RemainingVolumeM3 / capacity.VanVolumeM3)
                           + demandWeight * (van.RemainingWeightKg / capacity.VanWeightKg);

            if (score > bestScore)
            {
                bestScore = score;
                bestVan = van;
            }
        }

        if (bestVan is null) return false;

        bestVan.Load(index, package);
        return true;
    }

    /// <summary>
    /// Dosypání: zbylou kapacitu dodávek nabídneme dosud nenaloženým zásilkám sestupně podle
    /// výnosnosti. Tím se vrátí zpět to, co zaokrouhlení na celé dodávky ukouslo.
    /// </summary>
    /// <remarks>
    /// Aby průchod stovkami tisíc zbylých zásilek nestál 120 porovnání za každou z nich,
    /// držíme si největší volné místo napříč flotilou. Zásilka, která se do něj nevejde,
    /// je zamítnuta v konstantním čase; když volné místo klesne pod nejmenší zásilku dávky,
    /// průchod končí.
    /// </remarks>
    private static int TopUp(
        ReadOnlySpan<Package> packages, int[] rankedOrder, bool[] isPlaced, Van[] vans, FleetCapacity capacity)
    {
        (double smallestVolume, double smallestWeight) = SmallestTransportable(packages, capacity);
        (double freeVolume, double freeWeight) = LargestFreeSpace(vans);

        int added = 0;

        foreach (int index in rankedOrder)
        {
            if (freeVolume < smallestVolume || freeWeight < smallestWeight) break; // flotila je plná

            if (isPlaced[index]) continue;

            ref readonly Package package = ref packages[index];
            if (!capacity.IsTransportable(package)) continue;
            if (package.VolumeM3 > freeVolume || package.WeightKg > freeWeight) continue;

            if (!TryPlace(vans, index, package, capacity)) continue;

            isPlaced[index] = true;
            added++;
            (freeVolume, freeWeight) = LargestFreeSpace(vans);
        }

        return added;
    }

    private static (double Volume, double Weight) LargestFreeSpace(Van[] vans)
    {
        double volume = 0, weight = 0;
        foreach (Van van in vans)
        {
            if (van.RemainingVolumeM3 > volume) volume = van.RemainingVolumeM3;
            if (van.RemainingWeightKg > weight) weight = van.RemainingWeightKg;
        }

        return (volume, weight);
    }

    private static (double Volume, double Weight) SmallestTransportable(
        ReadOnlySpan<Package> packages, FleetCapacity capacity)
    {
        double volume = double.MaxValue, weight = double.MaxValue;
        foreach (ref readonly Package package in packages)
        {
            if (!capacity.IsTransportable(package)) continue;
            if (package.VolumeM3 < volume) volume = package.VolumeM3;
            if (package.WeightKg < weight) weight = package.WeightKg;
        }

        return (volume, weight);
    }

    /// <summary>Největší zásilky (v tom rozměru, který je pro ně těsnější) nakládáme první.</summary>
    private static int[] OrderByDecreasingSize(
        ReadOnlySpan<Package> packages, int[] selectedIndices, FleetCapacity capacity)
    {
        int[] order = (int[])selectedIndices.Clone();
        var key = new double[order.Length];

        for (int i = 0; i < order.Length; i++)
        {
            ref readonly Package package = ref packages[order[i]];
            key[i] = -Math.Max(package.VolumeM3 / capacity.VanVolumeM3, package.WeightKg / capacity.VanWeightKg);
        }

        Array.Sort(key, order);
        return order;
    }

    private static LoadPlan BuildPlan(
        Van[] vans, SelectionResult selection, FleetCapacity capacity,
        int loadedCount, int unplacedFromSelection, int topUpCount)
    {
        double revenue = 0, volume = 0, weight = 0;
        foreach (Van van in vans)
        {
            revenue += van.RevenueCzk;
            volume += capacity.VanVolumeM3 - van.RemainingVolumeM3;
            weight += capacity.VanWeightKg - van.RemainingWeightKg;
        }

        return new LoadPlan
        {
            Capacity = capacity,
            Vans = vans,
            Selection = selection,
            LoadedPackageCount = loadedCount,
            RevenueCzk = revenue,
            VolumeM3 = volume,
            WeightKg = weight,
            UnplacedFromSelectionCount = unplacedFromSelection,
            TopUpCount = topUpCount,
        };
    }
}
