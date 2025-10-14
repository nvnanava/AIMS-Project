namespace AIMS.ViewModels.Home
{
    public class AssetCardVm
    {
        public string AssetType { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string IconUrl { get; set; } = "/images/asset-icons/blank-icon.png";
        public string DetailsHref { get; set; } = "#";
    }
}
