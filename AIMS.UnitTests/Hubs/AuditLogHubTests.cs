using System.Collections.Concurrent;
using System.Reflection;

using AIMS.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIMS.UnitTests.Hubs
{
    public class AuditLogHubTests
    {
        private readonly Mock<IGroupManager> _mockGroups;
        private readonly Mock<HubCallerContext> _mockContext;
        private readonly Mock<IHubCallerClients> _mockClients;
        private readonly Mock<ILogger<AuditLogHub>> _mockLogger;
        private readonly AuditLogHub _hub;

        public AuditLogHubTests()
        {
            _mockGroups = new Mock<IGroupManager>();
            _mockClients = new Mock<IHubCallerClients>();
            _mockContext = new Mock<HubCallerContext>();
            _mockLogger = new Mock<ILogger<AuditLogHub>>();

            _hub = new AuditLogHub(_mockLogger.Object)
            {
                Groups = _mockGroups.Object,
                Clients = _mockClients.Object,
                Context = _mockContext.Object
            };
        }

        // Helper for expression-tree-friendly logger verification
        private static bool MatchesDisconnectedLog(object state, string connectionId)
        {
            // Rendered message text (safe fallback if state isn't a KV list)
            var text = state?.ToString() ?? string.Empty;

            // Must mention "disconnected" (case-insensitive) AND the connectionId
            var hasWords =
                text.Contains("disconnected", StringComparison.OrdinalIgnoreCase) &&
                text.Contains(connectionId, StringComparison.Ordinal);

            // Prefer structured logging: look for UtcTs key with DateTime/DateTimeOffset
            var hasStructuredTs = false;
            if (state is IEnumerable<KeyValuePair<string, object>> kvps)
            {
                foreach (var kv in kvps)
                {
                    if (kv.Key == "UtcTs" && (kv.Value is DateTimeOffset || kv.Value is DateTime))
                    {
                        hasStructuredTs = true;
                        break;
                    }
                }
            }

            // Fallback: try to parse a "... at <timestamp>" suffix in the formatted message
            var hasParsedTs = false;
            if (!hasStructuredTs && text.Contains(" at ", StringComparison.Ordinal))
            {
                var parts = text.Split(new[] { " at " }, StringSplitOptions.None);
                var ts = parts.Length > 1 ? parts[^1].Trim() : string.Empty;
                if (!string.IsNullOrEmpty(ts) && DateTimeOffset.TryParse(ts, out var _))
                {
                    hasParsedTs = true;
                }
            }

            return hasWords && (hasStructuredTs || hasParsedTs);
        }

        private static bool MatchesConnectedLog(object state, string connectionId)
        {
            // Avoid ?. and ?? — use explicit checks
            var textObj = state == null ? string.Empty : state.ToString();
            var text = textObj ?? string.Empty;

            return text.Contains("Connected", StringComparison.OrdinalIgnoreCase)
                && text.Contains(connectionId, StringComparison.Ordinal);
        }

        // OnConnected joins audit + logs with user context
        [Fact]
        public async Task OnConnectedAsync_CallsAddToGroupOnce_AndLogsConnectedEvent()
        {
            var connectionId = "conn-123";
            _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

            await _hub.OnConnectedAsync();

            _mockGroups.Verify(g => g.AddToGroupAsync(connectionId, "audit", default), Times.Once);
            _mockLogger.Verify(
                l => l.Log(
                    It.Is<LogLevel>(lvl => lvl == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) => MatchesConnectedLog(state, connectionId)),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());
        }

        // OnDisconnected leaves audit + logs with timestamp + ConnId
        [Fact]
        public async Task OnDisconnectedAsync_CallsRemoveFromGroupOnce_AndLogsDisconnectedEvent()
        {
            var connectionId = "conn-456";
            _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

            await _hub.OnDisconnectedAsync(null);

            _mockGroups.Verify(g => g.RemoveFromGroupAsync(connectionId, "audit", default), Times.Once);

            _mockLogger.Verify(l => l.Log(
                It.Is<LogLevel>(lvl => lvl == LogLevel.Information),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => MatchesDisconnectedLog(state, connectionId)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce());
        }

        // JoinAuditGroup explicit call is idempotent
        [Fact]
        public async Task JoinAuditGroup_Twice_DoesNotDuplicateMembership()
        {
            var connectionId = "conn-789";
            _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);

            await _hub.JoinAuditGroup();
            await _hub.JoinAuditGroup();

            // Idempotent — only one add across two calls
            _mockGroups.Verify(g => g.AddToGroupAsync(connectionId, "audit", default), Times.Once);
        }

        // Authorization attribute at class level; none override/disable it
        [Fact]
        public void AuditLogHub_HasAuthorizeAttribute_AndNotOverriddenInDerivedClasses()
        {
            var hubType = typeof(AuditLogHub);

            // Class-level [Authorize]
            var authorizeAttr = hubType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false);
            Assert.NotEmpty(authorizeAttr);

            // No derived class disables or redefines
            foreach (var derivedType in hubType.Assembly.GetTypes())
            {
                if (derivedType.IsSubclassOf(hubType))
                {
                    var derivedAllow = derivedType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false);
                    Assert.Empty(derivedAllow);
                    var derivedAuthorize = derivedType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false);
                    Assert.Empty(derivedAuthorize);
                }
            }

            // No hub method disables authorization with [AllowAnonymous]
            foreach (var m in hubType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var allowAnon = m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false);
                Assert.Empty(allowAnon);
            }
        }

        // Concurrency & group integrity (no dupes/orphans)
        [Fact]
        public async Task MultipleConcurrentConnections_MaintainGroupIntegrity()
        {
            var totalConnections = 25;
            var added = new ConcurrentBag<string>();
            var removed = new ConcurrentBag<string>();
            var tasks = new List<Task>();

            for (int i = 0; i < totalConnections; i++)
            {
                var id = $"conn-{i + 1}";
                var mockGroups = new Mock<IGroupManager>();
                mockGroups
                    .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), "audit", default))
                    .Callback<string, string, CancellationToken>((connId, _, __) => added.Add(connId))
                    .Returns(Task.CompletedTask);
                mockGroups
                    .Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), "audit", default))
                    .Callback<string, string, CancellationToken>((connId, _, __) => removed.Add(connId))
                    .Returns(Task.CompletedTask);

                var mockContext = new Mock<HubCallerContext>();
                mockContext.Setup(c => c.ConnectionId).Returns(id);

                var mockLogger = new Mock<ILogger<AuditLogHub>>();
                var hub = new AuditLogHub(mockLogger.Object)
                {
                    Groups = mockGroups.Object,
                    Context = mockContext.Object
                };

                tasks.Add(Task.Run(async () =>
                {
                    await hub.OnConnectedAsync();
                    await Task.Delay(10);
                    await hub.OnDisconnectedAsync(null);
                }));
            }

            await Task.WhenAll(tasks);
            await Task.Delay(50);

            Assert.Equal(totalConnections, added.Count);
            Assert.Equal(totalConnections, removed.Count);

            // No duplicates — distinct counts equal total
            Assert.Equal(totalConnections, added.Distinct().Count());
            Assert.Equal(totalConnections, removed.Distinct().Count());

            foreach (var id in Enumerable.Range(1, totalConnections).Select(x => $"conn-{x}"))
            {
                Assert.Contains(id, added);
                Assert.Contains(id, removed);
            }
        }
    }
}
