using System.ComponentModel.DataAnnotations;

namespace AIMS.ViewModels;

public sealed class UpsertThresholdDto
{
    [Required, MaxLength(32)]
    public string AssetType { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int ThresholdValue { get; set; }
}

public sealed class ThresholdVm
{
    public required string AssetType { get; init; }
    public int ThresholdValue { get; init; }
}
