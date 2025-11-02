using AIMS.Tests.Integration;

public sealed class APiTestFixture : IAsyncLifetime, ICollectionFixture<APiTestFixture>
{
    public APIWebApplicationFactory<Program> _webFactory { get; private set; } = default!;
    public DbTestHarness _harness { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // First, set up the external resource
        _harness = new DbTestHarness()
        {
            AutoDelete = false
        };
        await _harness.InitializeAsync();

        // Then, set up the WebApplicationFactory using the resource's state
        _webFactory = new APIWebApplicationFactory<Program>();
        _webFactory.SetHarness(_harness);
    }

    public async Task DisposeAsync()
    {
        // Dispose the WebApplicationFactory first (guard in case init failed)
        if (_webFactory is not null)
            await _webFactory.DisposeAsync();

        // Then, dispose the external resource
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}
