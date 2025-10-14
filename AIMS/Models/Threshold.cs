using System.ComponentModel.DataAnnotations;

namespace AIMS.Models;

public class Threshold
{
    public int ThresholdID { get; set; }

    [Required, MaxLength(32)]
    public string AssetType { get; set; } = string.Empty;

    public int ThresholdValue { get; set; }
}
