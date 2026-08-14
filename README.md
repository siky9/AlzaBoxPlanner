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
  je další volání téhož kódu nad zbytkem plus nově naskladněnými zásilkami.

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
sestupně podle skóre, dokud se vejdou. Správné θ hledáme **půlením intervalu**, dokud se
oba zdroje nevyčerpají současně (12 kroků).

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

## 4. Kvalita řešení – změřená, ne tvrzená

Program u každého plánování vypíše **horní mez optima** z Lagrangeovy relaxace:

```
L(λ) = λᵥ·V + λ_w·W + Σ max(0, výnos − λᵥ·objem − λ_w·hmotnost)
```

Pro libovolná nezáporná λ je to platná horní mez; multiplikátory bereme z kritické (první
odmítnuté) zásilky hladového průchodu, takže odhad vyjde těsný a stojí jeden průchod O(n).
Rozdíl proti dosaženému výnosu je certifikát – kolik Kč nám nejvýš uniklo.

**Výsledky pro 300 000 zásilek** (Release, jedno jádro, seed 42):

| profil | úzké hrdlo | θ | využití objem / nosnost | odstup od optima | čas |
|---|---|---|---|---|---|
| `mixed` (256 kg/m³) | objem | 1,0 | 100,00 % / 32,96 % | **0,012 %** (5 017 Kč) | 277 ms |
| `heavy` (876 kg/m³) | obojí | 0,883 | 100,00 % / 100,00 % | **0,015 %** (6 084 Kč) | 704 ms |
| `light` (100 kg/m³) | objem | 1,0 | 99,99 % / 11,44 % | **0,020 %** (8 223 Kč) | 287 ms |

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

## 5. Výkon

Pro statisíce zásilek není rychlost úzké hrdlo – jedno `Array.Sort` nad 300 000 klíči trvá
desítky ms. Šlo hlavně o to nepřijít o to zbytečně:

- zásilky jsou pole `readonly record struct` (sekvenční v paměti, žádný tlak na GC),
- řadí se pole indexů proti poli klíčů, samotné struktury se nikdy nepřesouvají,
- hledání θ recykluje předalokovaná pole a mezi „aktuálním“ a „dosud nejlepším“ výsledkem
  jen přehazuje reference – nealokuje ani nekopíruje,
- v horkých smyčkách žádné LINQ.

Kdyby časové okno bylo přísnější, další krok by bylo hledat θ na **vzorku** dávky
(θ je globální cena, z 10 % dat se odhadne dobře) a plný průchod pustit jen jednou na závěr –
to by nejhorší případ srazilo zhruba na pětinu. Zatím to nebylo potřeba.

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
5. **Zásilky jsou nezávislé** – žádná objednávka nemusí jet pohromadě. Rozšíření je snadné:
   objednávka se sloučí do jedné složené položky.
6. **Obě denní jízdy se plánují nezávisle**, nenaložené zásilky se přesouvají do další.
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
  Domain/         Package, FleetCapacity, Van, LoadPlan
  Selection/      IPackageSelector, LagrangianGreedySelector, LagrangianBound, BatchStatistics
  Assignment/     VanAssigner
  Validation/     LoadPlanValidator
  DeliveryPlanner.cs      fasáda: Plan(zásilky, kapacita) → LoadPlan
src/AlzaBox.Planner.Cli/  generátor dat, srovnávací strategie, report
tests/                    30 testů – hrubá síla na malých instancích, platnost horní meze,
                          proveditelnost plánu, počet vyjetých dodávek, determinismus
```

Obě fáze jsou za rozhraním, takže jdou vyměnit nezávisle (`PrioritySelector` v CLI je toho
příkladem – naivní strategie prochází přesně stejnou nakládací fází).

### Přepínače CLI

```
-n, --packages <počet>              počet generovaných zásilek (výchozí 300000)
-p, --profile  <mixed|heavy|light>  profil nabídky (výchozí mixed)
-s, --seed     <číslo>              seed generátoru (výchozí 42)
    --verify                        ověřit proveditelnost výsledného plánu
    --vans                          vypsat náplň prvních deseti dodávek
```

Případ „úterý a čtvrtek“ se ukáže třeba na `--packages 20000`: vejde se všechno, odstup od
optima je 0,0000 % a použije se **86 dodávek ze 120** – přesně tolik, kolik jich objem
nákladu vyžaduje. Na `--packages 1000` vyjede dodávek pět, na `--packages 100` jediná.
