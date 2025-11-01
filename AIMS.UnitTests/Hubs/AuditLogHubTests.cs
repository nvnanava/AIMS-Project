using Xunit;
using Moq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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

            Assert.True(order.IndexOf("AddGroup") > order.IndexOf("Log"), "Log should execute before AddToGroupAsync");
        }

        [Fact]
        public void AuditLogHub_HasAuthorizeAttribute_AndNotOverriddenInDerivedClasses()
        {
            var hubType = typeof(AuditLogHub);
            var authorizeAttr = hubType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false);
            Assert.NotEmpty(authorizeAttr);

            foreach (var derivedType in hubType.Assembly.GetTypes())
            {
                if (derivedType.IsSubclassOf(hubType))
                {
                    var derivedAttrs = derivedType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: false);
                    Assert.Empty(derivedAttrs);

                    var overrideAuthorize = derivedType.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false);
                    Assert.Empty(overrideAuthorize);
                }
            }
        }

        [Fact]
        public async Task MultipleConcurrentConnections_MaintainGroupIntegrity()
        {
            var connectionIds = new List<string>();
            var added = new HashSet<string>();
            var removed = new HashSet<string>();
            var locker = new object();

            _mockGroups
                .Setup(g => g.AddToGroupAsync(It.IsAny<string>(), "audit", default))
                .Callback<string, string, System.Threading.CancellationToken>((id, _, __) =>
                {
                    lock (locker) { added.Add(id); }
                })
                .Returns(Task.CompletedTask);

            _mockGroups
                .Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), "audit", default))
                .Callback<string, string, System.Threading.CancellationToken>((id, _, __) =>
                {
                    lock (locker) { removed.Add(id); }
                })
                .Returns(Task.CompletedTask);

            var tasks = new List<Task>();
            var random = new Random();
            for (int i = 0; i < 25; i++)
            {
                var id = $"conn-{random.Next(1000, 9999)}";
                connectionIds.Add(id);
                _mockContext.Setup(c => c.ConnectionId).Returns(id);
                tasks.Add(_hub.OnConnectedAsync());
            }

            await Task.WhenAll(tasks);
            Assert.Equal(25, added.Count);

            tasks.Clear();
            foreach (var id in connectionIds)
            {
                _mockContext.Setup(c => c.ConnectionId).Returns(id);
                tasks.Add(_hub.OnDisconnectedAsync(null));
            }

            await Task.WhenAll(tasks);
            Assert.Equal(25, removed.Count);

            foreach (var id in connectionIds)
            {
                Assert.Contains(id, added);
                Assert.Contains(id, removed);
            }

            _mockGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), "audit", default), Times.Exactly(25));
            _mockGroups.Verify(g => g.RemoveFromGroupAsync(It.IsAny<string>(), "audit", default), Times.Exactly(25));
        }
    }
}

