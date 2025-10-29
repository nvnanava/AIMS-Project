using System.Collections.Generic;
using System.Threading.Tasks;
using AIMS.Contracts;
using AIMS.Services;

namespace AIMS.UnitTests.Infrastructure
{
    /// <summary>
    /// Simple broadcaster that records the last event for assertions.
    /// </summary>
    public sealed class FakeAuditEventBroadcaster : IAuditEventBroadcaster
    {
        public readonly List<AuditEventDto> Events = new();

        public Task BroadcastAsync(AuditEventDto dto)
        {
            Events.Add(dto);
            return Task.CompletedTask;
        }
    }
}
