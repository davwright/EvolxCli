using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public class EnvUrlTests
{
    [Theory]
    [InlineData("osis-dev.crm4", "https://osis-dev.crm4.dynamics.com")]
    [InlineData("osis-dev.crm4.dynamics.com", "https://osis-dev.crm4.dynamics.com")]
    [InlineData("https://osis-dev.crm4.dynamics.com", "https://osis-dev.crm4.dynamics.com")]
    [InlineData("https://osis-dev.crm4.dynamics.com/", "https://osis-dev.crm4.dynamics.com")]
    [InlineData("http://osis-dev.crm4.dynamics.com", "https://osis-dev.crm4.dynamics.com")] // forced upgrade
    [InlineData("  osis-dev.crm4  ", "https://osis-dev.crm4.dynamics.com")] // whitespace tolerated
    public void Normalize_resolves_to_canonical_https_url(string input, string expected)
    {
        EnvUrlResolver.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_throws_on_empty(string? input)
    {
        Action act = () => EnvUrlResolver.Normalize(input!);
        act.Should().Throw<ArgumentException>();
    }
}
