namespace AIMS.ViewModels;

public sealed class AssetsPagePayloadVm
{
    public PersonVm? Supervisor { get; init; }
    public List<PersonVm> Reports { get; init; } = new();
    public List<AssetRowVm> Items { get; init; } = new();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
