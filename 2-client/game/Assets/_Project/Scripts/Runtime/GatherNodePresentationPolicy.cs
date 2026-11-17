using System;
namespace Pogranichye.Client
{
    public static class GatherNodePresentationPolicy
    {
        public const double PickRangeMeters = 3.0;
        public const int MaxVisibleNodes = 6;
        public static bool IsValid(string id,string kind,double distanceMeters,double remaining)
        {
            return !string.IsNullOrWhiteSpace(id) && id.StartsWith("g:",StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(kind) && IsFinite(distanceMeters) && distanceMeters >= 0d &&
                IsFinite(remaining) && remaining >= 0d;
        }
        public static bool CanPick(bool serverSaysHere,double distanceMeters,double remaining)
        {
            return serverSaysHere && IsFinite(distanceMeters) && distanceMeters >= 0d &&
                distanceMeters <= PickRangeMeters && IsFinite(remaining) && remaining >= 1d;
        }
        public static string KindName(string kind)
        {
            switch(kind) {
                case "herb": return "дикорастущая трава";
                case "mushroom": return "грибы";
                case "berries": return "ягодный куст";
                case "hive": return "дикий улей";
                case "deadwood": return "сухостой";
                case "nest": return "птичье гнездо";
                case "flax": return "дикий лён";
                default: return "находка";
            }
        }
        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}