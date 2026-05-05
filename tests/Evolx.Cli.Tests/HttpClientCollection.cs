using Xunit;

namespace Evolx.Cli.Tests;

/// <summary>
/// Test classes that swap the static HttpGateway client opt into this collection so
/// they run sequentially. xUnit serializes within a collection but parallelizes across
/// collections, so without this two tests can race the global handler swap.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HttpClientCollection : ICollectionFixture<HttpClientCollection>
{
    public const string Name = nameof(HttpClientCollection);
}
