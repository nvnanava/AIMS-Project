namespace AIMS.Dtos.Assets;

public sealed class AssetLiteDto
{
    public string? Name { get; set; }
    public string? Kind { get; set; }   // "Hardware" | "Software"
    public string? Tag { get; set; }   // SerialNumber (HW) | LicenseKey (SW)
}
