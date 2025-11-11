using AIMS.Dtos.Dashboard;

namespace AIMS.Services
{
    public interface ISummaryCardService
    {
        Task<List<SummaryCardDto>> GetSummaryAsync(IEnumerable<string>? types = null, CancellationToken ct = default);
        void InvalidateSummaryCache();
    }
}
