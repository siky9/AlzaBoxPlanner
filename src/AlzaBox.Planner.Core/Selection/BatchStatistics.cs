using AlzaBox.Planner.Core.Domain;

namespace AlzaBox.Planner.Core.Selection;

/// <summary>
/// Souhrn nabídky zásilek z jediného průchodu. Slouží k rozhodnutí, jestli je vůbec co
/// optimalizovat, a k levným zkratkám uvnitř hladového průchodu.
/// </summary>
internal readonly record struct BatchStatistics(
    int TransportableCount,
    double TotalVolumeM3,
    double TotalWeightKg,
    double TotalRevenueCzk,
    double MinVolumeM3,
    double MinWeightKg,
    double MaxVolumeM3,
    double MaxWeightKg)
{
    public static BatchStatistics Collect(ReadOnlySpan<Package> packages, FleetCapacity capacity)
    {
        int transportable = 0;
        double totalVolume = 0, totalWeight = 0, totalRevenue = 0;
        double minVolume = double.MaxValue, minWeight = double.MaxValue;
        double maxVolume = 0, maxWeight = 0;

        foreach (ref readonly Package package in packages)
        {
            if (!capacity.IsTransportable(package)) continue;

            transportable++;
            totalVolume += package.VolumeM3;
            totalWeight += package.WeightKg;
            totalRevenue += package.RevenueCzk;

            if (package.VolumeM3 < minVolume) minVolume = package.VolumeM3;
            if (package.WeightKg < minWeight) minWeight = package.WeightKg;
            if (package.VolumeM3 > maxVolume) maxVolume = package.VolumeM3;
            if (package.WeightKg > maxWeight) maxWeight = package.WeightKg;
        }

        return transportable == 0
            ? new BatchStatistics(0, 0, 0, 0, 0, 0, 0, 0)
            : new BatchStatistics(transportable, totalVolume, totalWeight, totalRevenue,
                                  minVolume, minWeight, maxVolume, maxWeight);
    }
}
