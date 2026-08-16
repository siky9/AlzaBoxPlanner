using System.Diagnostics;
using System.Globalization;
using System.Text;
using AlzaBox.Planner.Cli;
using AlzaBox.Planner.Core;
using AlzaBox.Planner.Core.Assignment;
using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Selection;
using AlzaBox.Planner.Core.Validation;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");

if (!Options.TryParse(args, out Options options, out string? error))
{
    Console.Error.WriteLine(error);
    Options.PrintUsage(Console.Error);
    return 1;
}

if (options.Help)
{
    Options.PrintUsage(Console.Out);
    return 0;
}

FleetCapacity capacity = FleetCapacity.Default;

Console.WriteLine($"Generuji {options.PackageCount:N0} zásilek (profil {options.Profile}, seed {options.Seed}) …");
Package[] packages = PackageGenerator.Generate(options.PackageCount, options.Profile, options.Seed);

// Plánovač je bezstavový; fáze voláme zvlášť jen kvůli oddělenému měření času.
// Produkční kód by použil rovnou DeliveryPlanner.Plan(packages, capacity).
var selector = new LagrangianGreedySelector();
var assigner = new VanAssigner();

// Celý den: dodávky objedou okruh vícekrát a každá další jízda veze to, co zbylo.
if (options.Rounds > 1)
{
    var dayStopwatch = Stopwatch.StartNew();
    DayPlan day = new DeliveryPlanner(selector, assigner).PlanDay(packages, capacity, options.Rounds);
    TimeSpan dayTime = dayStopwatch.Elapsed;

    PlanReport.PrintDay(packages, day, dayTime);
    if (options.ShowVans) PlanReport.PrintVanDetail(day.Rounds[0], limit: 10);

    return options.Verify ? PrintVerification(LoadPlanValidator.Validate(packages, day)) : 0;
}

var stopwatch = Stopwatch.StartNew();
SelectionResult selection = selector.Select(packages, capacity);
TimeSpan selectionTime = stopwatch.Elapsed;

stopwatch.Restart();
LoadPlan plan = assigner.Assign(packages, selection, capacity);
TimeSpan assignmentTime = stopwatch.Elapsed;

// Srovnávací strategie prohnané stejnou nakládací fází, aby se porovnávaly proveditelné plány.
// Stojí zhruba tolik co samotné plánování, takže jdou vypnout (`--no-baselines`) – měřené časy
// výš se jich netýkají tak jako tak.
var baselines = options.Baselines
    ? PrioritySelector.Baselines
        .Select(baseline => (baseline.Name, assigner.Assign(packages, baseline.Select(packages, capacity), capacity).RevenueCzk))
        .ToList()
    : [];

PlanReport.Print(packages, plan, selectionTime, assignmentTime, baselines);

if (options.ShowVans) PlanReport.PrintVanDetail(plan, limit: 10);

return options.Verify ? PrintVerification(LoadPlanValidator.Validate(packages, plan)) : 0;

static int PrintVerification(IReadOnlyList<string> problems)
{
    if (problems.Count == 0)
    {
        Console.WriteLine("Kontrola plánu: OK – žádná dodávka nepřekračuje kapacitu, žádná zásilka není naložena dvakrát.");
        return 0;
    }

    Console.Error.WriteLine($"Kontrola plánu našla {problems.Count} problémů:");
    foreach (string problem in problems.Take(20)) Console.Error.WriteLine($"  • {problem}");
    return 2;
}

/// <summary>Přepínače příkazové řádky.</summary>
internal sealed record Options(
    int PackageCount, BatchProfile Profile, int Seed, int Rounds,
    bool Verify, bool ShowVans, bool Baselines, bool Help)
{
    public static bool TryParse(string[] args, out Options options, out string? error)
    {
        int packageCount = 300_000;
        var profile = BatchProfile.Mixed;
        int seed = 42;
        int rounds = 1;
        bool verify = false, showVans = false, baselines = true, help = false;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string flag = args[i];

            switch (flag)
            {
                case "--verify": verify = true; continue;
                case "--vans": showVans = true; continue;
                case "--no-baselines": baselines = false; continue;
                case "--help" or "-h": help = true; continue;
            }

            // Hodnotu čteme bez posunu i. Kdyby se posouvalo uvnitř `when`, posun by proběhl
            // i u neúspěšné podmínky a chybová hláška by pak ukazovala na hodnotu místo přepínače.
            string? value = i + 1 < args.Length ? args[i + 1] : null;

            switch (flag)
            {
                case "--packages" or "-n" when int.TryParse(value, out int parsed) && parsed >= 0:
                    packageCount = parsed;
                    break;
                // Enum.TryParse spolkne i „-p 99“ – Enum.IsDefined to zachytí dřív,
                // než se z toho stane pád v generátoru.
                case "--profile" or "-p" when Enum.TryParse(value, ignoreCase: true, out BatchProfile parsed)
                                              && Enum.IsDefined(parsed):
                    profile = parsed;
                    break;
                case "--seed" or "-s" when int.TryParse(value, out int parsed):
                    seed = parsed;
                    break;
                case "--rounds" or "-r" when int.TryParse(value, out int parsed) && parsed >= 1:
                    rounds = parsed;
                    break;
                default:
                    error = $"Neznámý nebo neúplný přepínač: {flag}";
                    options = new Options(packageCount, profile, seed, rounds, verify, showVans, baselines, help);
                    return false;
            }

            i++; // hodnota je spotřebovaná
        }

        options = new Options(packageCount, profile, seed, rounds, verify, showVans, baselines, help);
        return true;
    }

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""

            Použití: alzabox-planner [přepínače]

              -n, --packages <počet>          počet generovaných zásilek (výchozí 300000)
              -p, --profile  <mixed|heavy|light|bulky>  profil nabídky (výchozí mixed)
              -s, --seed     <číslo>          seed generátoru (výchozí 42)
              -r, --rounds   <počet>          okruhů za den (výchozí 1, zadání počítá se 2)
                  --verify                    ověřit proveditelnost výsledného plánu
                  --vans                      vypsat náplň prvních deseti dodávek
                  --no-baselines              vynechat srovnání s naivními strategiemi
              -h, --help                      vypsat tuhle nápovědu
            """);
    }
}
