namespace AIMS.Dtos.Assets;

public class AssetLookupItemDto
{
    public int AssetID { get; set; }
    public string AssetName { get; set; } = "";
    public int AssetKind { get; set; } // 1 = Hardware, 2 = Software
}
