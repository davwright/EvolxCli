using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class ODataTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("can't", "can''t")]
    [InlineData("a'b'c", "a''b''c")]
    [InlineData("", "")]
    public void EscapeLiteral_doubles_single_quotes_per_OData_v4(string input, string expected)
    {
        OData.EscapeLiteral(input).Should().Be(expected);
    }
}
