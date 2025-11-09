using Xunit;

[CollectionDefinition("API Test Collection", DisableParallelization = true)]
public class APiTestCollection : ICollectionFixture<APiTestFixture>
{
    // This class is empty; defines the collection and fixtures.
}
