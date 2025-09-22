namespace AIMS.Helpers
{
    public static class ValidAssetTypes
    {
        // Replace with your real domain types if needed
        public static readonly string[] All = new[] { "hardware", "software" };

        public static bool IsValid(string? type) =>
            string.IsNullOrWhiteSpace(type) ||
            Array.Exists(All, t => t.Equals(type, StringComparison.OrdinalIgnoreCase));
    }
}
