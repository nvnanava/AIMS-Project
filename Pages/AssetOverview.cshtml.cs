using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AssetTrackingSystem.Pages
{
    public class AssetOverviewModel : PageModel
    {
        public string AssetName { get; set; } = "Default Asset Name";
        public string AssetType { get; set; } = "Type A";
        public string AssetStatus { get; set; } = "Active";
        public string Team { get; set; } = "Team X";

        public void OnGet()
        {
            // TODO: In the future, retrieve dynamic asset data from an API or database.
        }
    }
}

