using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Selection;

namespace AlzaBox.Planner.Core.Assignment;

/// <summary>
/// Rozdělení vybraných zásilek do konkrétních dodávek heuristikou <i>dot product</i>
/// pro vektorový bin-packing a následné dosypání zbytkové kapacity.
/// </summary>
/// <remarks>
/// <para>
/// Výběrová fáze počítá se souhrnnou kapacitou flotily, což je relaxace – teoreticky se
/// vybraná množina nemusí do 120 dodávek rozdělit beze zbytku. Protože je ale zásilka
/// o tři až čtyři řády menší než dodávka, je ztráta zaokrouhlením v praxi nulová
/// (řádově desítky zásilek ze statisíců).
/// </para>
/// <para>
/// Vyjíždí jen tolik dodávek, kolik je opravdu potřeba – viz <see cref="MinimumVanCount"/>.
/// V dnech, kdy se veze všechno (úterý a čtvrtek), tak nejede 120 poloprázdných vozidel.
/// </para>
/// </remarks>
public sealed class VanAssigner
{
    public LoadPlan Assign(ReadOnlySpan<Package> packages, SelectionResult selection, FleetCapacity capacity)
    {
        var vans = new Van[capacity.VanCount];
        for (int i = 0; i < vans.Length; i++) vans[i] = new Van(i, capacity);

        var isPlaced = new bool[packages.Length];
        int[] toPlace = OrderByDecreasingSize(packages, selection.SelectedIndices, capacity);

        // Nakládá se jen do „otevřených“ dodávek; další se otevře, teprve když se zásilka
        // nikam nevejde. Start na spodním odhadu, aby se náklad rovnoměrně rozprostřel
        // právě mezi ty dodávky, které stejně musí vyjet.
        int openVans = MinimumVanCount(selection, capacity);

        int placedCount = 0;
        foreach (int index in toPlace)
        {
            if (!TryPlace(vans, ref openVans, index, packages[index], capacity)) continue;
            isPlaced[index] = true;
            placedCount++;
        }

        int unplacedFromSelection = toPlace.Length - placedCount;
        int topUpCount = TopUp(packages, selection.RankedOrder, isPlaced, vans, openVans, capacity);

        return BuildPlan(vans, selection, capacity, placedCount + topUpCount, unplacedFromSelection, topUpCount);
    }

    /// <summary>
    /// Spodní mez počtu dodávek, do kterých se vybraná množina vůbec může vejít: obsah
    /// nelze rozdělit do méně vozidel, než kolik jich vyžaduje samotný objem nebo hmotnost.
    /// </summary>
    /// <remarks>
    /// V běžný den vyjde 120 (výběr flotilu naplní), takže se nakládá stejně jako předtím.
    /// V úterý a ve čtvrtek, kdy se veze celá nabídka, vyjde výrazně méně – a jen tolik
    /// dodávek se pak použije. Kdyby mez byla příliš optimistická (zaokrouhlení, nešikovný
    /// mix hustot), <see cref="TryPlace"/> otevře další dodávku.
    /// </remarks>
    private static int MinimumVanCount(SelectionResult selection, FleetCapacity capacity)
    {
        int byVolume = (int)Math.Ceiling(selection.VolumeM3 / capacity.VanVolumeM3);
        int byWeight = (int)Math.Ceiling(selection.WeightKg / capacity.VanWeightKg);

        return Math.Clamp(Math.Max(byVolume, byWeight), 1, capacity.VanCount);
    }

    /// <summary>
    /// Umístí zásilku do některé z otevřených dodávek; když se nevejde do žádné, otevře další.
    /// </summary>
    private static bool TryPlace(
        Van[] vans, ref int openVans, int index, in Package package, FleetCapacity capacity)
    {
        Van? bestVan = FindBestVan(vans, openVans, package, capacity);

        if (bestVan is null)
        {
            // Čerstvá dodávka pobere cokoli, co je vůbec přepravitelné.
            if (openVans >= vans.Length || !vans[openVans].CanFit(package)) return false;
            bestVan = vans[openVans++];
        }

        bestVan.Load(index, package);
        return true;
    }

    /// <summary>
    /// Vybere dodávku podle skalárního součinu poptávky a zbývající kapacity
    /// (heuristika „dot product“ pro vektorový bin-packing). Hledá jen mezi prvními
    /// <paramref name="openVans"/> dodávkami, aby zbytek flotily nemusel vyjíždět.
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
    private static Van? FindBestVan(Van[] vans, int openVans, in Package package, FleetCapacity capacity)
    {
        double demandVolume = package.VolumeM3 / capacity.VanVolumeM3;
        double demandWeight = package.WeightKg / capacity.VanWeightKg;

        Van? bestVan = null;
        double bestScore = double.NegativeInfinity;

        for (int i = 0; i < openVans; i++)
        {
            Van van = vans[i];
            if (!van.CanFit(package)) continue;

            double score = demandVolume * (van.RemainingVolumeM3 / capacity.VanVolumeM3)
                           + demandWeight * (van.RemainingWeightKg / capacity.VanWeightKg);

            if (score > bestScore)
            {
                bestScore = score;
                bestVan = van;
            }
        }

        return bestVan;
    }

    /// <summary>
    /// Dosypání: zbylou kapacitu už otevřených dodávek nabídneme dosud nenaloženým zásilkám
    /// sestupně podle výhodnosti. Pojistka pro případ, že nakládací fáze nechá použitelnou mezeru.
    /// </summary>
    /// <remarks>
    /// <para>
    /// V měřených dávkách (statisíce drobných zásilek) fáze nepřidá nic – nakládání doplní
    /// flotilu tak těsně, že z 840 m³ zbývají řádově setiny m³, tedy méně než nejmenší zásilka.
    /// Smysl má u dávek s hrubší zrnitostí, kde se za poslední velkou zásilku vejde ještě
    /// několik malých; stojí jeden průchod navíc, takže se vyplatí ji držet.
    /// </para>
    /// <para>
    /// Nové dodávky se tu neotevírají: kolik vozidel vyjede, rozhodla nakládací fáze a nemá
    /// smysl posílat další dodávku kvůli zásilkám, které výběr vyhodnotil jako nejméně výnosné.
    /// </para>
    /// <para>
    /// Aby průchod stovkami tisíc zbylých zásilek nestál jedno porovnání za každou dodávku,
    /// držíme si největší volné místo napříč flotilou. Zásilka, která se do něj nevejde,
    /// je zamítnuta v konstantním čase; když volné místo klesne pod nejmenší zásilku dávky,
    /// průchod končí.
    /// </para>
    /// </remarks>
    private static int TopUp(
        ReadOnlySpan<Package> packages, int[] rankedOrder, bool[] isPlaced,
        Van[] vans, int openVans, FleetCapacity capacity)
    {
        (double smallestVolume, double smallestWeight) = SmallestTransportable(packages, capacity);
        (double freeVolume, double freeWeight) = LargestFreeSpace(vans, openVans);

        int added = 0;

        foreach (int index in rankedOrder)
        {
            if (freeVolume < smallestVolume || freeWeight < smallestWeight) break; // flotila je plná

            if (isPlaced[index]) continue;

            ref readonly Package package = ref packages[index];
            if (!capacity.IsTransportable(package)) continue;
            if (package.VolumeM3 > freeVolume || package.WeightKg > freeWeight) continue;

            Van? van = FindBestVan(vans, openVans, package, capacity);
            if (van is null) continue;

            van.Load(index, package);
            isPlaced[index] = true;
            added++;
            (freeVolume, freeWeight) = LargestFreeSpace(vans, openVans);
        }

        return added;
    }

    private static (double Volume, double Weight) LargestFreeSpace(Van[] vans, int openVans)
    {
        double volume = 0, weight = 0;
        for (int i = 0; i < openVans; i++)
        {
            if (vans[i].RemainingVolumeM3 > volume) volume = vans[i].RemainingVolumeM3;
            if (vans[i].RemainingWeightKg > weight) weight = vans[i].RemainingWeightKg;
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
