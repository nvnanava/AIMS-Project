namespace AIMS.ViewModels.SummaryCards;

public class SummaryCardDto
{
    public string AssetType { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Available { get; set; }
    public int Threshold { get; set; }
    public bool IsLow { get; set; }
    public double AvailablePercent { get; set; }

}
