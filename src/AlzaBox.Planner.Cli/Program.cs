using System.Diagnostics;
using System.Globalization;
using System.Text;
using AlzaBox.Planner.Cli;
using AlzaBox.Planner.Core.Assignment;
using AlzaBox.Planner.Core.Domain;
using AlzaBox.Planner.Core.Selection;
using AlzaBox.Planner.Core.Validation;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");

if (!Options.TryParse(args, out Options options, out string? error))
{
    Console.Error.WriteLine(error);
    Options.PrintUsage();
    return 1;
}

FleetCapacity capacity = FleetCapacity.Default;

Console.WriteLine($"Generuji {options.PackageCount:N0} zásilek (profil {options.Profile}, seed {options.Seed}) …");
Package[] packages = PackageGenerator.Generate(options.PackageCount, options.Profile, options.Seed);

// Plánovač je bezstavový; fáze voláme zvlášť jen kvůli oddělenému měření času.
// Produkční kód by použil rovnou DeliveryPlanner.Plan(packages, capacity).
var selector = new LagrangianGreedySelector();
var assigner = new VanAssigner();

var stopwatch = Stopwatch.StartNew();
SelectionResult selection = selector.Select(packages, capacity);
TimeSpan selectionTime = stopwatch.Elapsed;

stopwatch.Restart();
LoadPlan plan = assigner.Assign(packages, selection, capacity);
TimeSpan assignmentTime = stopwatch.Elapsed;

// Srovnávací strategie prohnané stejnou nakládací fází, aby se porovnávaly proveditelné plány.
var baselines = PrioritySelector.Baselines
    .Select(baseline => (baseline.Name, assigner.Assign(packages, baseline.Select(packages, capacity), capacity).RevenueCzk))
    .ToList();

PlanReport.Print(packages, plan, selectionTime, assignmentTime, baselines);

if (options.ShowVans) PlanReport.PrintVanDetail(plan, limit: 10);

if (options.Verify)
{
    IReadOnlyList<string> problems = LoadPlanValidator.Validate(packages, plan);
    if (problems.Count == 0)
    {
        Console.WriteLine("Kontrola plánu: OK – žádná dodávka nepřekračuje kapacitu, žádná zásilka není naložena dvakrát.");
    }
    else
    {
        Console.Error.WriteLine($"Kontrola plánu našla {problems.Count} problémů:");
        foreach (string problem in problems.Take(20)) Console.Error.WriteLine($"  • {problem}");
        return 2;
    }
}

return 0;

/// <summary>Přepínače příkazové řádky.</summary>
internal sealed record Options(int PackageCount, BatchProfile Profile, int Seed, bool Verify, bool ShowVans)
{
    public static bool TryParse(string[] args, out Options options, out string? error)
    {
        int packageCount = 300_000;
        var profile = BatchProfile.Mixed;
        int seed = 42;
        bool verify = false;
        bool showVans = false;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--packages" or "-n" when i + 1 < args.Length && int.TryParse(args[++i], out int parsed):
                    packageCount = parsed;
                    break;
                case "--profile" or "-p" when i + 1 < args.Length && Enum.TryParse(args[++i], true, out BatchProfile parsedProfile):
                    profile = parsedProfile;
                    break;
                case "--seed" or "-s" when i + 1 < args.Length && int.TryParse(args[++i], out int parsedSeed):
                    seed = parsedSeed;
                    break;
                case "--verify":
                    verify = true;
                    break;
                case "--vans":
                    showVans = true;
                    break;
                default:
                    error = $"Neznámý nebo neúplný přepínač: {args[i]}";
                    options = new Options(packageCount, profile, seed, verify, showVans);
                    return false;
            }
        }

        options = new Options(packageCount, profile, seed, verify, showVans);
        return true;
    }

    public static void PrintUsage()
    {
        Console.Error.WriteLine("""

            Použití: alzabox-planner [přepínače]

              -n, --packages <počet>          počet generovaných zásilek (výchozí 300000)
              -p, --profile  <mixed|heavy|light>  profil nabídky (výchozí mixed)
              -s, --seed     <číslo>          seed generátoru (výchozí 42)
                  --verify                    ověřit proveditelnost výsledného plánu
                  --vans                      vypsat náplň prvních deseti dodávek
            """);
    }
}
