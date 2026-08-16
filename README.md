# Doručení teleportem – plánování svozu do AlzaBoxů

Řešení úlohy: z nabídky statisíců zásilek vybrat pro jeden okruh 120 dodávek takovou
podmnožinu, která má co nejvyšší výnosnost a zároveň se do dodávek reálně vejde.

```bash
dotnet run --project src/AlzaBox.Planner.Cli -c Release -- --verify
```

---

## 1. Jak jsem zadání pochopil

Napovídá už název úlohy – **teleport**. Geografie, pořadí zastávek ani trasování v zadání
nejsou a název říká, že tam ani nepatří. Dodávka tedy není vozidlo s trasou, ale **kontejner
se dvěma limity**, a všech 120 kontejnerů je stejných.

Zbývá tím čistě rozhodovací úloha: *vybrat z ~300 000 zásilek s trojicí (hmotnost, objem, výnos)
tu nejvýnosnější podmnožinu, která se vejde do 840 m³ a 660 t, a rozdělit ji do 120 dodávek.*

Dvě věty ze zadání jsem přečetl jako konkrétní požadavky:

- *„S výjimkou úterý a čtvrtku jezdíme s maximálním využitím kapacity.“* – normálně je
  poptávka větší než kapacita (zajímavý případ), ale program musí zvládnout i to, že se
  vejde všechno. To není jiný algoritmus, jen zkratka na začátku.
- *„Dvakrát denně.“* – jedno plánování = jeden okruh = 120 naložených dodávek. Druhá jízda
  je další volání téhož kódu nad zbytkem; `DeliveryPlanner.PlanDay` to řetězí za vás
  (viz [§3, fáze 4](#fáze-4--druhá-jízda)).

## 2. Model

Kapacita flotily na jeden okruh:

| | na dodávku | na flotilu |
|---|---|---|
| objem | 7 m³ | **840 m³** |
| hmotnost | 5 500 kg | **660 000 kg** |

Formálně jde o **dvourozměrný batoh** (0/1 knapsack se dvěma omezeními), který je NP-těžký,
plus bin-packing navrch. Konkrétní čísla ale úlohu výrazně zkrotí.

### Dvě pozorování, ze kterých vychází celé řešení

**(a) Které omezení je úzké hrdlo, se pozná z hustoty.** Poměr kapacit dává zlomovou hustotu
660 000 / 840 ≈ **786 kg/m³**. Běžná zásilka má 100–250 kg/m³, takže v praxi limituje objem
a nosnost je vlažná. Dávka plná baterií, nářadí nebo nápojů to ale otočí, takže na to
algoritmus nesmí spoléhat – musí si to zjistit z dat.

**(b) Zásilka je proti dodávce drobná.** Balík má řádově 0,001–0,1 m³ proti 7 m³ dodávky.
Výběr „co se poveze“ a rozdělení „čím se to poveze“ jdou proto rozpojit: skoro každá množina,
která se vejde do **souhrnné** kapacity, se dá do 120 dodávek rozdělit s téměř nulovým odpadem.

Z toho plyne rozdělení na dvě fáze:

1. **Výběr** proti souhrnné kapacitě flotily – tady jsou všechny peníze.
2. **Rozdělení** do konkrétních dodávek – jen otázka proveditelnosti.

## 3. Algoritmus

### Fáze 0 – filtr a zkratka
Zásilky, které se nevejdou ani do jedné dodávky (> 7 m³ nebo > 5,5 t), jdou pryč. Když se
celá nabídka vejde do flotily, veze se všechno a končíme – to je úterý a čtvrtek.

### Fáze 1 – výběr Lagrangeovým hladovým průchodem
Se dvěma omezeními nejde řadit prostě podle „výnos na m³“ – je potřeba jedno skóre, které
ocení oba zdroje:

```
cena(i, θ) = θ · objem(i)/840 + (1−θ) · hmotnost(i)/660000
skóre(i, θ) = výnos(i) / cena(i, θ)
```

θ je stínová cena zdrojů: říká, jestli je vzácnější místo, nebo nosnost. Zásilky se berou
sestupně podle skóre, dokud se vejdou. Správné θ hledáme **půlením intervalu** tak, aby se
oba zdroje vyčerpaly současně.

Půlení končí, jakmile je dosažený výnos blíž než 0,05 % k horní mezi (§4). Mez platí pro celou
úlohu, ne jen pro právě zkoušené θ, takže odstup od ní shora omezuje i to, co by našlo θ jiné –
**certifikát kvality tím slouží zároveň jako podmínka ukončení**, místo aby se ladil počet kroků.
Na těžké dávce to sráží 14 průchodů na 9 – tedy zhruba čtvrtinu času výběru – a vyjde přitom
θ i plán do koruny stejný jako při plném půlení. Strop 12 kroků zůstává jako pojistka.

Nejčastěji ale stačí **jediný průchod**: zkusí se θ = 1 (rozhoduje jen objem) a když při něm
nosnost nikoho neodmítla, je hmotnostní omezení neaktivní, úloha je jednorozměrná a hladový
výběr podle hustoty výnosu je optimem LP relaxace. Test je přesný, ne odhad – když je na
konci zbývající nosnost větší než nejtěžší zásilka dávky, nemohla nosnost nikdy nikoho
zablokovat. Totéž zrcadlově pro θ = 0. Teprve když jsou aktivní obě omezení, běží půlení.

**Proč to stačí:** LP relaxace batohu s *k* omezeními má v optimu nejvýš *k* zlomkové položky.
Pro k = 2 se hladové řešení liší od optima relaxace nejvýš o dvě zásilky – při 300 000 balících
zaokrouhlovací chyba. Metaheuristika (GA, žíhání, tabu) tu nemá co získat, jen by spálila
časové okno.

### Fáze 2 – rozdělení do dodávek
Zásilky se nakládají od největší (v tom rozměru, který je pro ně těsnější) a každá jde do
dodávky s **největším skalárním součinem** poptávky a zbývající kapacity – standardní
heuristika *dot product* pro vektorový bin-packing. Těžké zboží tak putuje do dodávek
s rezervou v nosnosti, objemné do dodávek s rezervou v místě.

> Tohle byl jediný netriviální krok při ladění. Původně tu byl klasický Best-Fit a na
> lehkém zboží fungoval dobře, ale na těžké dávce ztrácel **11,8 %** výnosu. Best-Fit cpe
> každou zásilku do nejplnější dodávky, takže se flotila rozdělila na dodávky vyčerpané na
> nosnost s volným místem a dodávky vyčerpané na objem s volnou nosností – a do takto
> polarizované flotily se pak nevešlo 18 361 vybraných zásilek. Skalární součin drží náklad
> každé dodávky blízko hustotě, při které dojdou oba zdroje naráz; nevešlo se pak 34 zásilek
> a ztráta klesla na 0,015 %.

Skalární součin ale sám o sobě míří do **nejprázdnější** vhodné dodávky, takže náklad rozptýlí
přes celou flotilu – sto zásilek by rozvezlo sto dodávek po jednom balíku. Proto se nakládá
jen do *otevřených* dodávek a jejich počet startuje na spodní mezi

```
minimum = max( ⌈objem výběru / 7⌉ , ⌈hmotnost výběru / 5500⌉ )
```

Do méně vozidel se náklad vejít nemůže, takže se tím nic neztrácí; další dodávka se otevře,
jakmile se zásilka do žádné otevřené nevejde. V běžný den vyjde minimum rovnou 120 a nakládá
se přesně jako předtím (ověřeno: výnos je na korunu shodný). V úterý a ve čtvrtek se ale
z garáže vyjíždí jen tolik dodávek, kolik je opravdu potřeba.

### Fáze 3 – dosypání
Zbylá kapacita otevřených dodávek se nabídne dosud nenaloženým zásilkám sestupně podle
výhodnosti. Průchod je levný: největší volné místo napříč flotilou se drží stranou, takže se
zásilka, která se nevejde nikam, zamítne v konstantním čase.

Na měřených dávkách tahle fáze **nepřidá nic** – nakládání zaplní flotilu tak těsně, že
z 840 m³ zbývají řádově setiny m³, tedy míň než nejmenší zásilka v nabídce. Smysl má u dávek
s hrubší zrnitostí, kde se za poslední velkou zásilku vejde ještě několik malých. Za jeden
průchod navíc je to levná pojistka, takže v kódu zůstává – ale výsledky v tabulce níž jí
nevděčí za nic.

Že to není mrtvý kód, hlídají testy z obou stran (`VanAssignerTests`): na drobném mixu musí
vyjít nula, a na dávce s hrubou zrnitostí – deset dodávek, 25 zásilek po 2,5 m³ (do dodávky
se vejdou dvě a 2 m³ zbudou) plus 300 drobných – musí fáze vrátit do hry **všech 150** drobných
zásilek, které výběr odmítl.

### Fáze 4 – druhá jízda

Okruh je jednotka optimalizace, ale den má podle zadání jízdy dvě. Nakládací fáze proto vrací
i `LoadPlan.UnloadedIndices` – co zůstalo ve skladu, seřazené podle výhodnosti. To je přesně
nabídka další jízdy a `DeliveryPlanner.PlanDay(zásilky, kapacita, rounds: 2)` z toho udělá
celý den:

```
okruh   zásilek    objem    nosnost   dodávek        výnos Kč     odstup
    1    71 706   100,0 %    33,0 %   120 / 120      40 873 922    0,0123 %
    2    46 667   100,0 %    32,7 %   120 / 120      19 613 459    0,0105 %
```

Druhá jízda veze o třetinu míň balíků při stejném objemu – ráno se sebraly ty drobné
a nejvýnosnější na m³, odpoledne jedou větší. Výnos druhé jízdy je proto **necelá polovina**
té první; to je vlastnost úlohy, ne slabina plánovače (odstup od optima zůstává na 0,01 %).

Okruhy se plánují **hladově za sebou**, ne společně: druhá jízda dostane až to, co první
nechala. Optimum celého dne by se hledalo jinak – jenže obě jízdy dohromady jsou proti nabídce
pořád jen 1 680 m³ z potřebných ~3 500, takže omezení zůstává stejné a společné plánování by
posunulo jen hranici, kde se řez vede. Za dvojnásobek času to nestojí.

Má to jeden důsledek pro certifikát: `DayPlan.GapCzk` je **součet odstupů po okruzích**, tedy
ztráta výběru měřená v každé jízdě proti její vlastní nabídce – ne mez proti optimálnímu
rozvržení celého dne. Ta by se dokazovala hůř: nabídky okruhů nejsou disjunktní, ale vnořené,
zatímco optimální den se naší volbou první jízdy vázat nemusí, a přímočará úvaha dá jen
`mez₁ + 2·mez₂`. Na 2 000 malých instancích proti optimu z hrubé síly (dva i tři okruhy, pět
různých rozdělení) mez nepadla ani jednou a v nejtěsnějším případě seděla přesně na optimu –
takže ověřeno, ne dokázáno, a v reportu je to popsané jako „ztráta výběru“, ne jako odstup od
optima dne.

Druhý okruh navíc plánuje jen nad zbytkem (228 000 položek místo 300 000), takže stojí míň než
první – celý den vyjde zhruba na jeden a půl násobku jednoho okruhu, ne na dvojnásobku. Když
se sklad vyprázdní dřív, další jízda se nenaplánuje vůbec (`-n 20000 -r 2` vyjede jediný okruh
o 86 dodávkách); stejně tak skončí den okruhem, který nenaložil nic, protože ze zbytku už
flotila neuveze vůbec nic.

Doskladňování mezi jízdami je stejné volání: nabídkou další jízdy je `UnloadedIndices`
doplněné o indexy novinek, na to je přímo přetížení
`Plan(zásilky, nabídka, kapacita)`. Indexy v plánu vždycky ukazují do celého skladu, takže
se okruhy dají skládat a `LoadPlanValidator` umí ověřit, že žádná zásilka nejede dvakrát.

## 4. Kvalita řešení – změřená, ne tvrzená

Program u každého plánování vypíše **horní mez optima** z Lagrangeovy relaxace:

```
L(λ) = λᵥ·V + λ_w·W + Σ max(0, výnos − λᵥ·objem − λ_w·hmotnost)
```

Pro libovolná nezáporná λ je to platná horní mez; multiplikátory bereme z kritické (první
odmítnuté) zásilky hladového průchodu, takže odhad vyjde těsný a stojí jeden průchod O(n).
Rozdíl proti dosaženému výnosu je certifikát – kolik Kč nám nejvýš uniklo.

**Výsledky pro 300 000 zásilek** (Release, seed 42):

| profil | úzké hrdlo | θ | využití objem / nosnost | odstup od optima | čas |
|---|---|---|---|---|---|
| `mixed` (256 kg/m³) | objem | 1,0 | 100,00 % / 32,96 % | **0,012 %** (5 017 Kč) | ~210 ms |
| `heavy` (876 kg/m³) | obojí | 0,883 | 100,00 % / 100,00 % | **0,015 %** (6 084 Kč) | ~600 ms |
| `light` (100 kg/m³) | objem | 1,0 | 99,99 % / 11,44 % | **0,020 %** (8 223 Kč) | ~210 ms |
| `bulky` (kusové) | objem | 1,0 | 94,55 % / 15,26 % | 5,622 % → viz níž | ~120 ms |

Odstup, využití i θ jsou deterministické – vyjdou na každém stroji stejně. **Časy jsou
orientační**: pocházejí z jednoho běžného notebooku, měří jen plánování (bez generování dat
a bez srovnávacích strategií, `--no-baselines`) a mezi běhy kolísají o jednotky procent.
Absolutní hodnoty tedy neberte doslova; nosné je, že se všechny vejdou hluboko pod sekundu
a jak se k sobě mají navzájem.

Srovnání s naivními strategiemi (všechny prohnané **stejnou** nakládací fází, aby se
porovnávaly proveditelné plány):

| strategie | `mixed` | `heavy` |
|---|---|---|
| nejdražší zásilky první | −84 % | −99 % |
| nejvyšší výnos na m³ | shodně | −6,0 % |
| nejvyšší výnos na kg | −11,5 % | −30,8 % |

Na běžném mixu je vidět, že řazení podle výnosu na m³ je prakticky optimální – protože
limituje jen objem. Hodnota hledání stínové ceny se ukáže až na těžké dávce, kde jsou
aktivní obě omezení. Právě proto to řešení hledá, místo aby si vybralo dopředu.

Ověřuje se i proveditelnost: `--verify` zkontroluje, že žádná dodávka nepřekračuje kapacitu,
žádná zásilka není naložena dvakrát a výnos nepřekračuje horní mez.

### Kdy je odstup ztráta a kdy jen volná mez

Mez počítá se souhrnnou kapacitou, jako by byl náklad tekutina. Pro drobné zásilky to sedí,
pro kusové zboží ne – a tam přestává být odstup údaj o ztrátě. Plán proto nese verdikt
(`LoadPlan.Verdict`), který ten rozdíl pojmenuje. Rozhoduje se podle otázky **„unesla by flotila
ještě něco z toho, co zbylo?“**, ne podle procenta využití; nízké využití samo o sobě neznamená
nic:

| verdikt | co znamená | jak číst odstup |
|---|---|---|
| `NothingLeftToCarry` | nic přepravitelného nezbylo | nula |
| `Saturated` | úzké hrdlo je vyčerpané | skutečná ztráta, setiny % |
| `GranularityLimited` | místo zbylo, ale nic se do něj nevejde | **nadhodnocuje** ztrátu |
| `SpaceLeftUnused` | zbylo místo *i* zásilky do něj | chyba nakládání – hlásí `--verify` |

Na profilu `bulky` (kusové zboží 0,6–3 m³ plus 2 % kusů přes 7 m³) to vidět je: objem skončí
na 94,5 %, odstup vyjde
5,62 % – a program k tomu rovnou napíše, že do zbylých mezer se žádná zásilka nevejde a plán
je proti tomu, co flotila fyzicky uveze, mnohem lepší, než to číslo vypadá. Tentýž mechanismus
udrží klid u dávky samých zásilek po 3,6 m³: 51 % objemu, ale verdikt `NothingLeftToCarry` nebo
`GranularityLimited` podle toho, jestli ještě něco zbylo – žádné falešné varování.

Že práh na procentech nestačí, hlídá test `Nizke_vyuziti_samo_o_sobe_verdikt_neurcuje`: dvě
dávky se shodným využitím objemu musí dostat různý verdikt.

### Co mez neuvidí: nadměrné zboží

Zásilka přes 7 m³ nebo 5,5 t neprojde ani jednou dodávkou. Výběr ji odfiltruje hned na začátku
a horní mez s ní nepočítá – správně, protože mez má říkat, co uveze *tahle* flotila. Vedlejším
účinkem ale je, že sklad plný nadměrného zboží vypadá jako splněný plán: odstup od optima
**0,0000 %**, verdikt „nic přepravitelného nezbylo“, a padesát milionů leží ve skladu.

Proto plán takové zásilky počítá zvlášť (`LoadPlan.NonTransportableCount`) a report je hlásí
rovnou u výnosnosti. Nejsou to zásilky čekající na další okruh – ty potřebují jiné vozidlo
a druhá jízda s nimi nepohne, takže se den ukončí okruhem, který už nic nenaložil.

Profil `bulky` obsahuje 2 % zboží přes 7 m³ (sedačka, velká lednička), takže je to vidět rovnou:

```
⚠ 5 944 zásilek za 16 302 676 Kč neuveze žádná dodávka (> 7 m³ nebo > 5,5 t).
```

Šestnáct milionů, o kterých by odstup od optima mlčel – protože z pohledu téhle flotily
mlčet má. Rozhodnutí, co s nimi, je ale byznysové, ne algoritmické, a k tomu je potřeba
o nich vědět.

## 5. Výkon

Pro statisíce zásilek není rychlost úzké hrdlo – jedno `Array.Sort` nad 300 000 klíči trvá
desítky ms. Šlo hlavně o to nepřijít o to zbytečně:

- zásilky jsou pole `readonly record struct` (sekvenční v paměti, žádný tlak na GC),
- řadí se pole indexů proti poli klíčů, samotné struktury se nikdy nepřesouvají,
- hledání θ recykluje předalokovaná pole a mezi „aktuálním“ a „dosud nejlepším“ výsledkem
  jen přehazuje reference – nealokuje ani nekopíruje,
- v horkých smyčkách žádné LINQ.

### Kde by se dalo zrychlit dál – a proč jsem to neudělal

Změřeno, ne odhadnuto: jedno `Array.Sort` nad 300 000 klíči stojí zhruba **31 ms**, zatímco
ohodnocení téhož pole (lineární průchod s dělením) necelou milisekundu. Na profilu `heavy`
tedy z času výběru padne zhruba **58 % na samotné řazení**.

Řadit se přitom nemusí všechno. Hladový průchod projde jen zásilky nad kritickým skóre plus
kousek pod ním – u `mixed` je to čtvrtina pole. Kdyby se pomocí *quickselectu* našel práh
a seřadil jen ten prefix (se záložní cestou, kdyby prefix nestačil), spadlo by řazení zhruba
na čtvrtinu a s ním i většina času plánování na profilu `heavy`.

**Přesto to v kódu není**, ze dvou důvodů:

1. `Array.Sort` není stabilní, takže seřazení prefixu rozhodne shodná skóre jinak než seřazení
   celku. Výsledek by byl stejně dobrý, ale **jiný** – a přepsala by se tím všechna čísla
   v téhle dokumentaci kvůli zrychlení, které nikdo nepotřebuje.
2. Byl by to nejsubtilnější kód v celém řešení (odhad prahu, partition, záložní dořazení)
   umístěný přesně tam, kde chyba stojí nejvíc – a to kvůli času, který se i v nejhorším
   případě vejde do 0,6 s.

Je to připravená rezerva pro případ, že by dávky vyrostly o řád: milion zásilek zvládne
současný kód za ~1,4 s, s prefixovým řazením by to bylo pod sekundu. Do zadání
(„řádově statisíce“) se ale pohodlně vejdeme i bez toho.

Ze stejného soudku: **zhuštění nepřepravitelných zásilek** na začátku by se dalo čekat jako
levná výhra, ale není – v reálných profilech je nepřepravitelných zásilek nula, takže by se
ušetřilo jen porovnání na položku a průchod by se nezkrátil ani o jednu.

## 6. Předpoklady a zjednodušení

1. **Bez trasování a geografie** – dodávky jsou kontejnery („teleport“).
2. **Objem je aditivní** – žádné 3D balení, dle zadání se rozměry neřeší.
3. **Výnosnost je kladná**, každá přepravitelná zásilka se vejde do jedné dodávky.
4. **Zásilka je proti dodávce drobná** (pozorování (b) výše). Na tom stojí rozpojení výběru
   a nakládání. Kdyby dávka byla samé zboží velikosti dodávky, rozdíl mezi souhrnnou kapacitou
   a skutečným bin-packingem přestane být zaokrouhlovací: 400 zásilek po 3,6 m³ souhrnná
   kapacita připouští 233, ale do 7m³ dodávky se vejde jen jedna, takže se odveze 120 kusů.
   Plán je i tak **optimální** (víc jich flotila neuveze) – volná je v tomhle režimu horní mez,
   ne řešení. Hlášený odstup od optima tedy zůstává poctivý horní odhad ztráty, jen přestává
   být těsný. Pro reálný mix AlzaBoxů (setiny až desetiny m³) to nenastává.

   Tenhle předpoklad je jediný, který se dá dávkou porušit, aniž by to bylo vidět – proto si ho
   program **hlídá sám** a neschovává ho do dokumentace: viz verdikt kapacity v §4 a profil
   `bulky`. Co program nedělá, je pokus tu ztrátu dohnat; šlo by to (vybírat rovnou s ohledem na
   to, jak se zboží skládá, místo dvou oddělených fází), ale byla by to jiná úloha než ta zadaná
   a pro reálnou zrnitost AlzaBoxů by se nevrátila.
5. **Zásilky jsou nezávislé** – žádná objednávka nemusí jet pohromadě. Rozšíření je snadné:
   objednávka se sloučí do jedné složené položky.
6. **Obě denní jízdy se plánují za sebou, ne společně** – druhá dostane to, co první nechala
   (`PlanDay`, viz fáze 4). Nové zboží naskladněné mezi jízdami se přidá do nabídky té druhé.
7. Peníze jsou `double`, ne `decimal` – pro plánovací výpočet nad statisíci položkami je
   přesnost hluboko pod haléřem a `decimal` by v horké smyčce zbytečně brzdil.

### Jedna poznámka mimo zadání

Čistá maximalizace výnosu **nechá levné zásilky ležet natrvalo**. Balík za 50 Kč nemusí být
vybrán žádný den, protože se nikdy nedostane před dražší. Reálně by do skóre patřil bonus za
stáří (`efektivní_výnos = výnos + α · dny_čekání`) nebo tvrdý příznak „musí jet“ po N dnech –
jinak se SLA rozjede přesně u těch zákazníků, kteří si toho všimnou nejvíc. Zadání říká
maximalizovat výnosnost, takže cíl nechávám tak, jak je; kód je ale postavený tak, aby byla
skórovací funkce na jednom místě (`LagrangianGreedySelector`, výpočet `key`) a šla vyměnit.

## 7. Zvážené alternativy

| přístup | proč ne |
|---|---|
| **Přesné DP** | Kapacity jsou spojité, stavový prostor při 300 000 položkách neprůchodný. |
| **MILP solver** (OR-Tools, CBC) | Správné, ale na 300 000 binárních proměnných daleko za časovým oknem a je to další závislost. Použitelné leda jako offline kontrola kvality na malých instancích. |
| **Genetika / žíhání / tabu** | Nejefektnější a tady nejhorší volba: časové okno to nedovolí a změřený odstup od horní meze ukazuje, že není co získat. Vědět, kdy metaheuristiku nepoužít, je součást řešení. |
| **Batoh zvlášť pro každou dodávku** | Víc práce, horší výsledek – dodávky jsou identické, nemá smysl je řešit odděleně. |

## 8. Struktura řešení

```
src/AlzaBox.Planner.Core/
  Domain/         Package, FleetCapacity, Van, LoadPlan, DayPlan, CapacityVerdict
  Selection/      IPackageSelector, LagrangianGreedySelector, LagrangianBound, BatchStatistics
  Assignment/     VanAssigner
  Validation/     LoadPlanValidator
  DeliveryPlanner.cs      fasáda: Plan(zásilky, [nabídka,] kapacita) → LoadPlan
                                  PlanDay(zásilky, kapacita, okruhů)  → DayPlan
src/AlzaBox.Planner.Cli/  generátor dat, srovnávací strategie, report
tests/                    47 testů – hrubá síla na malých instancích, platnost horní meze,
                          proveditelnost plánu, počet vyjetých dodávek, determinismus, dosypání,
                          verdikt kapacity, nepřekrývání okruhů dne
```

Obě fáze jsou za rozhraním, takže jdou vyměnit nezávisle (`PrioritySelector` v CLI je toho
příkladem – naivní strategie prochází přesně stejnou nakládací fází).

### Přepínače CLI

```
-n, --packages <počet>              počet generovaných zásilek (výchozí 300000)
-p, --profile  <mixed|heavy|light|bulky>  profil nabídky (výchozí mixed)
-s, --seed     <číslo>              seed generátoru (výchozí 42)
-r, --rounds   <počet>              okruhů za den (výchozí 1, zadání počítá se 2)
    --verify                        ověřit proveditelnost výsledného plánu
    --vans                          vypsat náplň prvních deseti dodávek
    --no-baselines                  vynechat srovnání s naivními strategiemi
-h, --help                          vypsat nápovědu
```

Výchozí `--rounds 1` je záměr: jednotkou optimalizace je jeden okruh a všechna čísla výš platí
pro něj. Celý den ukáže `--rounds 2`; s `--verify` se u něj navíc kontroluje, že se okruhy
nepřekrývají.

Případ „úterý a čtvrtek“ se ukáže třeba na `--packages 20000`: vejde se všechno, odstup od
optima je 0,0000 % a použije se **86 dodávek ze 120** – přesně tolik, kolik jich objem
nákladu vyžaduje. Na `--packages 1000` vyjede dodávek pět, na `--packages 100` jediná.
