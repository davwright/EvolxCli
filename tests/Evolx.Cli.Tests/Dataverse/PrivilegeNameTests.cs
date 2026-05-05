using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class PrivilegeNameTests
{
    [Theory]
    [InlineData("prvReadAccount", "Read", "Account")]
    [InlineData("prvCreateContact", "Create", "Contact")]
    [InlineData("prvWriteEvo_site", "Write", "Evo_site")]
    [InlineData("prvAppendToOpportunity", "AppendTo", "Opportunity")]
    [InlineData("prvAppendOpportunity", "Append", "Opportunity")]
    [InlineData("prvDeleteSystemUser", "Delete", "SystemUser")]
    public void Split_extracts_action_and_table(string input, string expectedAction, string expectedTable)
    {
        var (action, table) = PrivilegeName.Split(input);
        action.Should().Be(expectedAction);
        table.Should().Be(expectedTable);
    }

    [Fact]
    public void Split_returns_empty_for_unknown_shape()
    {
        var (action, _) = PrivilegeName.Split("notAprvName");
        action.Should().BeEmpty();
    }

    [Fact]
    public void Split_returns_empty_action_when_unknown_action_word()
    {
        var (action, table) = PrivilegeName.Split("prvFooAccount");
        action.Should().BeEmpty();
        table.Should().Be("prvFooAccount"); // when unmatched, hand back original so caller can debug
    }

    [Theory]
    [InlineData(1, "User")]
    [InlineData(2, "BU")]
    [InlineData(4, "Parent BU")]
    [InlineData(8, "Org")]
    [InlineData(0, "(none)")]
    [InlineData(15, "mask=15")]
    public void DepthLabel_renders_known_masks(int mask, string expected)
    {
        PrivilegeName.DepthLabel(mask).Should().Be(expected);
    }
}
