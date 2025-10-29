using System.Threading.Tasks;
using AIMS.Contracts;
using AIMS.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIMS.Services;

public interface IAuditEventBroadcaster
{
    Task BroadcastAsync(AuditEventDto dto);
}

public sealed class AimsFeatures
{
    public bool AuditRealTime { get; set; } = true;
    public bool AuditPollingFallback { get; set; } = true;
}

public static class Telemetry
{
    public static readonly System.Diagnostics.Metrics.Meter Meter =
        new("AIMS", "1.0.0");

    public static readonly System.Diagnostics.Metrics.Counter<long> AuditBroadcasted =
        Meter.CreateCounter<long>("audit_events_broadcasted_total");

    public static readonly System.Diagnostics.Metrics.Counter<long> AuditPollRequests =
        Meter.CreateCounter<long>("audit_poll_requests_total");

    public static readonly System.Diagnostics.Metrics.Counter<long> AuditPollEtagHits =
        Meter.CreateCounter<long>("audit_poll_etag_hits_total");
}

public sealed class AuditEventBroadcaster : IAuditEventBroadcaster
{
    private readonly IHubContext<AuditLogHub> _hub;
    private readonly ILogger<AuditEventBroadcaster> _log;
    private readonly IOptions<AimsFeatures> _features;

    public AuditEventBroadcaster(IHubContext<AuditLogHub> hub,
                                 ILogger<AuditEventBroadcaster> log,
                                 IOptions<AimsFeatures> features)
    {
        _hub = hub;
        _log = log;
        _features = features;
    }

    public async Task BroadcastAsync(AuditEventDto dto)
    {
        if (!_features.Value.AuditRealTime) return;

        await _hub.Clients.Group("audit").SendAsync("auditEvent", dto);
        Telemetry.AuditBroadcasted.Add(1);
        _log.LogDebug("Broadcasted audit event {Id} ({Type}) at {At}", dto.Id, dto.Type, dto.OccurredAtUtc);
    }
}
