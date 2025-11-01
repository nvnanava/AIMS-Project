using Xunit;
using Moq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Hubs;

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
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Connected", StringComparison.OrdinalIgnoreCase)),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task OnDisconnectedAsync_CallsRemoveFromGroupOnce_AndLogsDisconnectedEvent()
        {
            var connectionId = "conn-456";
            _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);
            await _hub.OnDisconnectedAsync(null);
            _mockGroups.Verify(g => g.RemoveFromGroupAsync(connectionId, "audit", default), Times.Once);
            _mockLogger.Verify(
                l => l.Log(
                    It.Is<LogLevel>(lvl => lvl == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Disconnected", StringComparison.OrdinalIgnoreCase)),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.AtLeastOnce());
        }

        [Fact]
        public async Task JoinAuditGroup_CalledTwice_AddsToGroupTwice_AndAddsBeforeLogging()
        {
            var connectionId = "conn-789";
            _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);
            await _hub.JoinAuditGroup();
            await _hub.JoinAuditGroup();
            _mockGroups.Verify(g => g.AddToGroupAsync(connectionId, "audit", default), Times.Exactly(2));
            var order = new List<string>();
            _mockGroups
                .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default))
                .Callback(() => order.Add("AddGroup"))
                .Returns(Task.CompletedTask);
            _mockLogger
                .Setup(l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                .Callback(() => order.Add("Log"));
            await _hub.JoinAuditGroup();
            Assert.True(order.IndexOf("AddGroup") > order.IndexOf("Log"));
        }

        [Fact]
        public void AuditLogHub_HasAuthorizeAttribute_AndNotOverriddenInDerivedClasses()
        {
            var hubType = typeof(AuditLogHub);
            var authorizeAttr = hubType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false);
            Assert.NotEmpty(authorizeAttr);
            foreach (var derivedType in hubType.Assembly.GetTypes())
            {
                if (derivedType.IsSubclassOf(hubType))
                {
                    var derivedAttrs = derivedType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false);
                    Assert.Empty(derivedAttrs);
                    var overrideAuthorize = derivedType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false);
                    Assert.Empty(overrideAuthorize);
                }
            }
        }

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
            await Task.Delay(200);

            Assert.Equal(totalConnections, added.Count);
            Assert.Equal(totalConnections, removed.Count);
            foreach (var id in Enumerable.Range(1, totalConnections).Select(x => $"conn-{x}"))
            {
                Assert.Contains(id, added);
                Assert.Contains(id, removed);
            }
        }
    }
}

