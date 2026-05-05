using Evolx.Cli.Http;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Http;

public sealed class QueryStringTests
{
    [Fact]
    public void Empty_input_returns_empty_string()
    {
        QueryString.Build(Array.Empty<KeyValuePair<string, string?>>())
            .Should().BeEmpty();
    }

    [Fact]
    public void Drops_null_and_whitespace_values()
    {
        var result = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$filter", null),
            new("$select", ""),
            new("$top", "10"),
        });
        result.Should().Be("?$top=10");
    }

    [Fact]
    public void Percent_encodes_values_but_keeps_dollar_in_keys()
    {
        var result = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$filter", "name eq 'a b'"),
        });
        // $ is preserved in key; ' in value becomes %27, space becomes %20
        result.Should().Be("?$filter=name%20eq%20%27a%20b%27");
    }

    [Fact]
    public void Joins_multiple_params_with_ampersand()
    {
        var result = QueryString.Build(new KeyValuePair<string, string?>[]
        {
            new("$top", "5"),
            new("$select", "name,id"),
        });
        result.Should().Be("?$top=5&$select=name%2Cid");
    }
}
