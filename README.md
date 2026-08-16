# Doručení teleportem – plánování svozu do AlzaBoxů

Z nabídky statisíců zásilek vybrat pro jeden okruh 120 dodávek tu nejvýnosnější podmnožinu,
která se do nich reálně vejde.

```bash
dotnet run --project src/AlzaBox.Planner.Cli -c Release -- --verify
```

## Jak jsem zadání pochopil

Název úlohy je **teleport** – geografie ani trasování v zadání nejsou a název říká, že tam
nepatří. Dodávka proto není vozidlo s trasou, ale **kontejner se dvěma limity** (7 m³ a 5,5 t),
a všech 120 je stejných. Zbývá čistě rozhodovací úloha: vybrat z ~300 000 zásilek podmnožinu,
která se vejde do 840 m³ a 660 t, a rozdělit ji do 120 dodávek.

## Řešení

**Výběr – Lagrangeův hladový průchod.** Se dvěma omezeními nejde řadit podle „výnos na m³“; je
potřeba jedno skóre, které ocení oba zdroje:

```
skóre(i, θ) = výnos(i) / ( θ·objem(i)/840 + (1−θ)·hmotnost(i)/660000 )
```

θ je stínová cena zdrojů – říká, jestli je vzácnější místo, nebo nosnost. Zásilky se berou
sestupně podle skóre, dokud se vejdou; správné θ se hledá půlením intervalu. Nejčastěji stačí
**jediný průchod**: když při θ=1 nosnost nikoho neodmítla, je hmotnostní omezení neaktivní,
úloha je jednorozměrná a hladový výběr podle hustoty výnosu je optimem LP relaxace.

**Rozdělení – heuristika *dot product*.** Zásilky se nakládají od největší a každá jde do
dodávky s největším skalárním součinem poptávky a zbývající kapacity. Těžké zboží tak míří do
dodávek s rezervou v nosnosti, objemné do dodávek s rezervou v místě. Vyjíždí jen tolik
dodávek, kolik je potřeba; zbytková kapacita se nakonec nabídne nenaloženým zásilkám.

**Kvalita se měří, netvrdí.** Program u každého plánování vypíše horní mez z Lagrangeovy
relaxace (platná pro libovolná λ ≥ 0), takže rozdíl proti dosaženému výnosu je certifikát –
kolik Kč nejvýš uniklo. Týž certifikát slouží i jako podmínka ukončení půlení: jakmile je výnos
blíž než 0,05 % k mezi, nemá další θ co získat.

Na 300 000 zásilkách běžné zrnitosti se výběr drží do **0,02 % pod horní mezí** (`mixed` 0,012 %,
`heavy` 0,015 %) a celé plánování trvá 0,1–0,6 s podle profilu – časy jsou orientační, odstup
deterministický. `--verify` ověří proveditelnost plánu; 47 testů zahrnuje i srovnání s optimem
spočteným hrubou silou na malých instancích.

**Den má dvě jízdy.** `PlanDay` řetězí okruhy – další jízda dostane to, co předchozí nechala
(`--rounds 2`).

## Předpoklady a zjednodušení

- **Bez trasování a geografie** (teleport); objem je aditivní, rozměry se dle zadání neřeší.
- **Výnosnost je kladná** a zásilky jsou nezávislé (objednávka by se sloučila do jedné položky).
- **Peníze jsou `double`** – přesnost je hluboko pod haléřem a `decimal` by v horké smyčce brzdil.
- **Zásilka je proti dodávce drobná** (0,001–0,1 m³ proti 7 m³). Na tom stojí rozpojení
  výběru a nakládání: skoro každá množina, která se vejde do *souhrnné* kapacity, se dá do 120
  dodávek rozdělit s téměř nulovým odpadem. U kusového zboží to neplatí a rozdíl přestává být
  zaokrouhlovací – program to ale pozná sám a řekne (`LoadPlan.Verdict`, profil `bulky`): hlásí,
  že do zbylých mezer se už nic nevejde a odstup od meze tedy ztrátu **nadhodnocuje**. Stejně
  tak hlásí zásilky nad 7 m³ / 5,5 t, které neuveze žádná dodávka – ty se z úlohy jinak vytratí
  potichu, protože je výběr odfiltruje a mez s nimi nepočítá.
- **Obě denní jízdy** se plánují hladově za sebou, ne společně.

Mimo zadání: čistá maximalizace výnosu nechá levné zásilky ležet natrvalo. Reálně by do skóre
patřil bonus za stáří nebo tvrdý příznak „musí jet“ po N dnech. Zadání říká maximalizovat
výnos, tak to nechávám – skórovací funkce je ale na jednom místě a jde vyměnit.

## Struktura a přepínače

```
src/AlzaBox.Planner.Core/   Domain, Selection (výběr), Assignment (nakládání), Validation
src/AlzaBox.Planner.Cli/    generátor dat, srovnávací strategie, report
tests/                      47 testů
```

Obě fáze jsou za rozhraním, takže jdou vyměnit nezávisle.

```
-n, --packages <počet>                     počet zásilek (výchozí 300000)
-p, --profile  <mixed|heavy|light|bulky>   profil nabídky (výchozí mixed)
-s, --seed     <číslo>                     seed generátoru (výchozí 42)
-r, --rounds   <počet>                     okruhů za den (výchozí 1, zadání počítá se 2)
    --verify                               ověřit proveditelnost plánu
    --vans                                 vypsat náplň prvních deseti dodávek
    --no-baselines                         vynechat srovnání s naivními strategiemi
-h, --help                                 vypsat nápovědu
```
