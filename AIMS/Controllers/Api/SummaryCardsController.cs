using System.Net;
using AIMS.Dtos.Dashboard;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Controllers.Api;

[ApiController]
[Route("api/summary")]
public class SummaryCardsController : ControllerBase
{
    private readonly ISummaryCardService _svc;
    private readonly ILogger<SummaryCardsController> _logger;

    public SummaryCardsController(ISummaryCardService svc, ILogger<SummaryCardsController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    [HttpGet("cards")]
    [ProducesResponseType(typeof(IEnumerable<SummaryCardDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCards([FromQuery] string? types, CancellationToken ct = default)
    {
        try
        {
            List<string>? filter = null;

            if (!string.IsNullOrWhiteSpace(types))
            {
                var decoded = WebUtility.UrlDecode(types);
                filter = decoded
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (filter.Count == 0)
                    filter = null;
            }

            var rows = await _svc.GetSummaryAsync(filter, ct);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute summary cards for types='{Types}'", types);
            return Problem("Failed to compute summary cards.");
        }
    }
}
