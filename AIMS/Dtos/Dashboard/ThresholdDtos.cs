using System.ComponentModel.DataAnnotations;

namespace AIMS.Dtos.Dashboard;

public sealed class UpsertThresholdDto
{
    [Required, MaxLength(32)]
    public string AssetType { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int ThresholdValue { get; set; }
}
