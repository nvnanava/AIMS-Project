using AIMS.Dtos.Users;

namespace AIMS.Dtos.Assets;

public sealed class AssetsPagePayloadDto
{
    public PersonDto? Supervisor { get; init; }
    public List<PersonDto> Reports { get; init; } = new();
    public List<AssetRowDto> Items { get; init; } = new();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
