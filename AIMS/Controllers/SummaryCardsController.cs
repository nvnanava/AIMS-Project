using AIMS.Services;
using AIMS.ViewModels.SummaryCards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers;

[ApiController]
[Route("api/summary")]
[Authorize(Policy = "mbcAdmin")]
public class SummaryCardsController : ControllerBase
{
    private readonly SummaryCardService _svc;

    public SummaryCardsController(SummaryCardService svc)
    {
        _svc = svc;
    }

    // Returns summary card rows per asset type: Total, Available, Threshold, IsLow, AvailablePercent.
    // Optional filtering by comma-separated asset types.
    [HttpGet("cards")]
    [ProducesResponseType(typeof(IEnumerable<SummaryCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCards([FromQuery] string? types, CancellationToken ct = default)
    {
        List<string>? filter = null;
        if (!string.IsNullOrWhiteSpace(types))
        {
            filter = types
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (filter.Count == 0) filter = null;
        }

        var rows = await _svc.GetSummaryAsync(filter, ct);
        return Ok(rows);
    }
}
