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
    /// <summary>
    /// Od jakého využití bottlenecku považujeme flotilu za vyčerpanou. 99,9 % je necelá
    /// nedovezená m³ z 840 – pod tím už stojí za to ptát se proč.
    /// </summary>
    private const double SaturationThreshold = 0.999;

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
        int[] unloaded = CollectUnloaded(selection.RankedOrder, isPlaced);

        return BuildPlan(packages, vans, selection, capacity,
                         placedCount + topUpCount, unplacedFromSelection, topUpCount, unloaded);
    }

    /// <summary>
    /// Projde, co zůstalo ve skladu, a rozhodne, o co se plán zastavil. Klíčová otázka není
    /// „je flotila plná?“, ale <b>„unesla by ještě něco z toho, co zbylo?“</b> – nízké využití
    /// samo o sobě nic neznamená. Dávka samých zásilek po 3,6 m³ skončí na 51 % objemu a je
    /// přesto optimální: do zbylých 3,4 m³ se další 3,6m³ zásilka nevejde a víc jich flotila neuveze.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zároveň se tu sečtou <b>nepřepravitelné</b> zásilky. Ty se z úlohy vytrácejí potichu –
    /// výběr je odfiltruje, horní mez s nimi nepočítá, takže by se sklad plný zboží přes 7 m³
    /// tvářil jako splněný plán s nulovým odstupem od optima. Proto je plán hlásí zvlášť.
    /// </para>
    /// <para>
    /// Průchod je levný: největší volné místo napříč flotilou zamítne drtivou většinu zbytku
    /// v konstantním čase, a jakmile je jasné, že se ještě něco vejde, přestane se hledat úplně.
    /// </para>
    /// <para>
    /// <b>Proč se hledá přes všechny dodávky, i ty nevyjeté</b> (na rozdíl od <see cref="TopUp"/>,
    /// který zůstává u otevřených): ptáme se, co by flotila zvládla, ne co zvládlo nakládání.
    /// Rozejít se ty dva pohledy nemůžou – kdykoli výběr něco odmítne, muselo mu dojít místo
    /// nebo nosnost, takže platí <c>součet výběru &gt; kapacita − kapacita jedné dodávky</c>,
    /// a <see cref="MinimumVanCount"/> proto vyjde rovnou na plný počet dodávek. A zásilka se
    /// nevejde do dodávky jedině tehdy, když už jsou otevřené všechny. V obou případech je
    /// tedy „otevřené“ a „všechny“ totéž; <see cref="CapacityVerdict.SpaceLeftUnused"/> je díky
    /// tomu nedosažitelný a validátor ho může hlásit jako chybu.
    /// </para>
    /// </remarks>
    private static LeftoverSummary Judge(
        ReadOnlySpan<Package> packages, int[] unloaded, Van[] vans, FleetCapacity capacity,
        double volumeUtilization, double weightUtilization)
    {
        (double freeVolume, double freeWeight) = LargestFreeSpace(vans, vans.Length);

        int nonTransportableCount = 0;
        double nonTransportableRevenue = 0;
        bool carryableLeft = false;
        bool somethingStillFits = false;

        foreach (int index in unloaded)
        {
            ref readonly Package package = ref packages[index];

            if (!capacity.IsTransportable(package))
            {
                nonTransportableCount++;
                nonTransportableRevenue += package.RevenueCzk;
                continue; // potřebuje jiné vozidlo, ne místo v tomhle
            }

            carryableLeft = true;

            if (somethingStillFits) continue; // verdikt je jasný, dopočítáváme už jen nepřepravitelné
            if (package.VolumeM3 > freeVolume || package.WeightKg > freeWeight) continue;
            if (FindBestVan(vans, vans.Length, package, capacity) is not null) somethingStillFits = true;
        }

        CapacityVerdict verdict;
        if (somethingStillFits) verdict = CapacityVerdict.SpaceLeftUnused;
        else if (!carryableLeft) verdict = CapacityVerdict.NothingLeftToCarry;
        else if (Math.Max(volumeUtilization, weightUtilization) >= SaturationThreshold) verdict = CapacityVerdict.Saturated;
        else verdict = CapacityVerdict.GranularityLimited;

        return new LeftoverSummary(verdict, nonTransportableCount, nonTransportableRevenue);
    }

    /// <summary>Co zůstalo ve skladu a co z toho plyne pro čtení odstupu od horní meze.</summary>
    private readonly record struct LeftoverSummary(
        CapacityVerdict Verdict, int NonTransportableCount, double NonTransportableRevenueCzk);

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
        (double smallestVolume, double smallestWeight) = SmallestTransportable(packages, rankedOrder, capacity);
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

    /// <summary>
    /// Nejmenší zásilka nabídky – práh, pod kterým už dosypávání nemá co nabídnout.
    /// Prochází se <paramref name="offer"/>, ne celé pole: v druhém okruhu dne je nabídkou
    /// jen zbytek po prvním, takže zásilky odvezené ráno práh ovlivňovat nesmí.
    /// </summary>
    private static (double Volume, double Weight) SmallestTransportable(
        ReadOnlySpan<Package> packages, int[] offer, FleetCapacity capacity)
    {
        double volume = double.MaxValue, weight = double.MaxValue;
        foreach (int index in offer)
        {
            ref readonly Package package = ref packages[index];
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

    /// <summary>
    /// Co z nabídky zůstalo ve skladu. <c>RankedOrder</c> je celá nabídka tohoto okruhu seřazená
    /// podle výhodnosti, takže stačí vyfiltrovat nenaložené – a další okruh dostane nabídku,
    /// která už je v rozumném pořadí.
    /// </summary>
    private static int[] CollectUnloaded(int[] rankedOrder, bool[] isPlaced)
    {
        int count = 0;
        foreach (int index in rankedOrder)
        {
            if (!isPlaced[index]) count++;
        }

        var unloaded = new int[count];
        int next = 0;
        foreach (int index in rankedOrder)
        {
            if (!isPlaced[index]) unloaded[next++] = index;
        }

        return unloaded;
    }

    private static LoadPlan BuildPlan(
        ReadOnlySpan<Package> packages, Van[] vans, SelectionResult selection, FleetCapacity capacity,
        int loadedCount, int unplacedFromSelection, int topUpCount, int[] unloaded)
    {
        double revenue = 0, volume = 0, weight = 0;
        foreach (Van van in vans)
        {
            revenue += van.RevenueCzk;
            volume += capacity.VanVolumeM3 - van.RemainingVolumeM3;
            weight += capacity.VanWeightKg - van.RemainingWeightKg;
        }

        LeftoverSummary leftovers = Judge(packages, unloaded, vans, capacity,
                                          volume / capacity.TotalVolumeM3, weight / capacity.TotalWeightKg);

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
            UnloadedIndices = unloaded,
            Verdict = leftovers.Verdict,
            NonTransportableCount = leftovers.NonTransportableCount,
            NonTransportableRevenueCzk = leftovers.NonTransportableRevenueCzk,
        };
    }
}
