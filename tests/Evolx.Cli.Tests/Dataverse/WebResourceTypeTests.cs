using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public class WebResourceTypeTests
{
    [Theory]
    [InlineData("foo.js", 3)]
    [InlineData("path/to/foo.html", 1)]
    [InlineData("path/to/foo.htm", 1)]
    [InlineData("foo.css", 2)]
    [InlineData("a.xml", 4)]
    [InlineData("a.png", 5)]
    [InlineData("a.jpg", 6)]
    [InlineData("a.jpeg", 6)]
    [InlineData("a.gif", 7)]
    [InlineData("a.xap", 8)]
    [InlineData("a.xsl", 9)]
    [InlineData("a.xslt", 9)]
    [InlineData("a.ico", 10)]
    [InlineData("a.svg", 11)]
    [InlineData("a.resx", 12)]
    public void Maps_known_extensions(string path, int expected)
    {
        WebResourceType.FromPath(path).Should().Be(expected);
    }

    [Fact]
    public void Throws_when_extension_is_missing()
    {
        var act = () => WebResourceType.FromPath("noextension");
        act.Should().Throw<ArgumentException>().WithMessage("*no extension*");
    }

    [Fact]
    public void Throws_on_unknown_extension()
    {
        var act = () => WebResourceType.FromPath("foo.bogus");
        act.Should().Throw<ArgumentException>().WithMessage("*Unknown*");
    }

    [Theory]
    [InlineData("js", 3)]
    [InlineData("JS", 3)]
    [InlineData("html", 1)]
    public void FromName_is_case_insensitive(string name, int expected)
    {
        WebResourceType.FromName(name).Should().Be(expected);
    }
}
