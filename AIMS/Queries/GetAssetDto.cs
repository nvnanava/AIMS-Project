namespace AIMS.Queries
{
    public sealed class GetAssetDto
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = ""; // "Hardware" | "Software"
        public string Tag { get; set; } = ""; // SerialNumber or LicenseKey
    }
}
