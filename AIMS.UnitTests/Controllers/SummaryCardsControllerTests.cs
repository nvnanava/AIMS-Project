using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AIMS.Controllers.Api;
using AIMS.Dtos.Dashboard;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIMS.Tests.Api
{
    // Fake logger to capture errors
    public class FakeLogger<T> : ILogger<T>
    {
        public string? LastErrorMessage { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                LastErrorMessage = formatter(state, exception);
        }
    }

    public class SummaryCardsControllerTests
    {
        private readonly Mock<ISummaryCardService> _mockService;
        private readonly FakeLogger<SummaryCardsController> _logger;
        private readonly SummaryCardsController _controller;

        public SummaryCardsControllerTests()
        {

            _mockService = new Mock<ISummaryCardService>();
            _logger = new FakeLogger<SummaryCardsController>();
            _controller = new SummaryCardsController(_mockService.Object, _logger);
        }

        [Theory]
        [InlineData("DEVICES, Devices , reports,, Reports", new[] { "devices", "reports" })]
        [InlineData("hardware%2Csoftware", new[] { "hardware", "software" })] // encoded comma
        [InlineData("active, Active, inactive", new[] { "active", "inactive" })]
        public async Task GetCards_ShouldNormalizeAndDeduplicateTypes(string input, string[] expectedTypes)
        {
            // Arrange
            var returnData = expectedTypes.Select(t => new SummaryCardDto { AssetType = t, Total = 1 }).ToList();
            List<string>? capturedFilter = null;

            _mockService
                .Setup(s => s.GetSummaryAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(returnData)
                .Callback<IEnumerable<string>, CancellationToken>((filter, ct) =>
                {
                    capturedFilter = filter?.ToList();
                });

            // Act
            var result = await _controller.GetCards(input, CancellationToken.None);

            // Assert response
            var ok = Assert.IsType<OkObjectResult>(result);
            var data = Assert.IsAssignableFrom<IEnumerable<SummaryCardDto>>(ok.Value);
            Assert.Equal(expectedTypes.Length, data.Count());

            // Assert that filter passed to service is normalized + unique
            Assert.NotNull(capturedFilter);
            Assert.Equal(
                expectedTypes.OrderBy(s => s),
                capturedFilter!.Select(s => s.ToLower()).OrderBy(s => s)
            );
        }


        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" , , ")]
        public async Task GetCards_ShouldHandleOmittedOrBlankTypes(string? types)
        {
            // Arrange
            var seedData = new List<SummaryCardDto> { new() { AssetType = "All Assets", Total = 10 } };

            _mockService
                .Setup(s => s.GetSummaryAsync(null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(seedData);

            // Act
            var result = await _controller.GetCards(types, CancellationToken.None);

            // Assert
            var ok = Assert.IsType<OkObjectResult>(result);
            var data = Assert.IsAssignableFrom<IEnumerable<SummaryCardDto>>(ok.Value);
            Assert.Single(data);
            Assert.Equal("All Assets", data.First().AssetType);

            // Confirm GetSummaryAsync(null) was called exactly once
            _mockService.Verify(s => s.GetSummaryAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetCards_ShouldLogErrorAndReturnProblem_WhenServiceThrows()
        {
            // Arrange
            _mockService
                .Setup(s => s.GetSummaryAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Simulated failure"));

            // Act
            var result = await _controller.GetCards("hardware,software", CancellationToken.None);

            // Assert response is Problem (HTTP 500)
            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, problem.StatusCode);

            // Assert error was logged
            Assert.NotNull(_logger.LastErrorMessage);
            Assert.Contains("Failed to compute summary cards", _logger.LastErrorMessage);
        }
    }
}
