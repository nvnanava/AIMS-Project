using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AIMS.Hubs;

// Hub for real-time audit log streaming. Clients subscribe to the "audit" group and receive "auditEvent" messages.
[Authorize]
public class AuditLogHub : Hub
{
    private readonly ILogger<AuditLogHub> _logger;

    // Track joined connections to make JoinAuditGroup idempotent
    private static readonly ConcurrentDictionary<string, byte> _joinedConnections = new();

    public AuditLogHub(ILogger<AuditLogHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "audit");
        _joinedConnections.TryAdd(Context.ConnectionId, 0);
        _logger.LogInformation("AuditLogHub connected: {ConnId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "audit");
        _joinedConnections.TryRemove(Context.ConnectionId, out _);
        _logger.LogInformation("AuditLogHub disconnected: {ConnId} at {UtcTs}", Context.ConnectionId, DateTimeOffset.UtcNow);
        await base.OnDisconnectedAsync(ex);
    }

    // Idempotent: only add if not already joined.
    public Task JoinAuditGroup()
    {
        if (_joinedConnections.TryAdd(Context.ConnectionId, 0))
        {
            _logger.LogInformation("JoinAuditGroup called for {ConnId}", Context.ConnectionId);
            return Groups.AddToGroupAsync(Context.ConnectionId, "audit");
        }

        // Already joined; log once for visibility but don't re-add
        _logger.LogInformation("JoinAuditGroup skipped for already joined connection {ConnId}", Context.ConnectionId);
        return Task.CompletedTask;
    }
}
