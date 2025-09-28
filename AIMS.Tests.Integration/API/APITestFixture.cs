using AIMS.Tests.Integration;

public class APiTestFixture : IAsyncLifetime, ICollectionFixture<APiTestFixture>
{
    public APIWebApplicationFactory<Program> _webFactory { get; private set; }
    public DbTestHarness _harness { get; private set; }

    public async Task InitializeAsync()
    {
        // First, set up the external resource
        _harness = new DbTestHarness();
        _harness.AutoDelete = false;
        await _harness.InitializeAsync();

        // Then, set up the WebApplicationFactory using the resource's state
        _webFactory = new APIWebApplicationFactory<Program>();
        _webFactory.SetHarness(_harness);
    }

    public async Task DisposeAsync()
    {
        // Dispose the WebApplicationFactory first
        await _webFactory.DisposeAsync();

        // Then, dispose the external resource
        await _harness.DisposeAsync();
    }
}
