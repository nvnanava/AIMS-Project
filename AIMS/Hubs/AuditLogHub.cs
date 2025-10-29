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

    public AuditLogHub(ILogger<AuditLogHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "audit");
        _logger.LogInformation("AuditLogHub connected: {ConnId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(System.Exception? ex)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "audit");
        _logger.LogInformation("AuditLogHub disconnected: {ConnId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(ex);
    }

    // Explicit join if we ever want per-tenant rooms later.
    public Task JoinAuditGroup() => Groups.AddToGroupAsync(Context.ConnectionId, "audit");
}
