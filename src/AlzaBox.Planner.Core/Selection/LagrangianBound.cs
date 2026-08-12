using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Core.Selection;

/// <summary>
/// Horní odhad dosažitelné výnosnosti pomocí Lagrangeovy relaxace. Slouží jako certifikát
/// kvality – říká, o kolik Kč se naše řešení nejvýš liší od skutečného optima.
/// </summary>
/// <remarks>
/// <para>
/// Pro libovolné nezáporné multiplikátory λ platí, že
/// <c>L(λ) = λᵥ·V + λ_w·W + Σ max(0, výnos − λᵥ·objem − λ_w·hmotnost)</c>
/// je horní mezí úlohy. Nezávisí to na tom, jak dobré λ zvolíme – lepší λ dá jen těsnější mez.
/// </para>
/// <para>
/// Multiplikátory bereme z hladového průchodu: kritická (první odmítnutá) zásilka má skóre
/// <c>s* = výnos/cena</c> a zásilka se bere právě tehdy, když <c>výnos − s*·cena(θ) ≥ 0</c>.
/// Rozepsáním ceny vyjde λᵥ = s*·θ/V a λ_w = s*·(1−θ)/W, takže první dva členy dají rovnou s*.
/// </para>
/// </remarks>
internal static class LagrangianBound
{
    public static double Evaluate(
        ReadOnlySpan<Package> packages, FleetCapacity capacity, double theta, double criticalScore)
    {
        if (criticalScore <= 0) return SumRevenue(packages, capacity); // nic se neodmítlo

        double lambdaVolume = criticalScore * theta / capacity.TotalVolumeM3;
        double lambdaWeight = criticalScore * (1.0 - theta) / capacity.TotalWeightKg;

        double bound = criticalScore; // λᵥ·V + λ_w·W

        foreach (ref readonly Package package in packages)
        {
            if (!capacity.IsTransportable(package)) continue;

            double reducedRevenue = package.RevenueCzk
                                    - lambdaVolume * package.VolumeM3
                                    - lambdaWeight * package.WeightKg;
            if (reducedRevenue > 0) bound += reducedRevenue;
        }

        return bound;
    }

    private static double SumRevenue(ReadOnlySpan<Package> packages, FleetCapacity capacity)
    {
        double total = 0;
        foreach (ref readonly Package package in packages)
        {
            if (capacity.IsTransportable(package)) total += package.RevenueCzk;
        }

        return total;
    }
}
