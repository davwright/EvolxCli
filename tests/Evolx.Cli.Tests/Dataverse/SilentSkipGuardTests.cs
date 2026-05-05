using Evolx.Cli.Dataverse;
using FluentAssertions;
using Xunit;

namespace Evolx.Cli.Tests.Dataverse;

public sealed class SilentSkipGuardTests
{
    [Fact]
    public async Task Returns_when_verify_is_true()
    {
        var mutated = false;
        await SilentSkipGuard.RunAsync(
            "test mutation",
            mutate: () => { mutated = true; return Task.CompletedTask; },
            verify: () => Task.FromResult(true));

        mutated.Should().BeTrue();
    }

    [Fact]
    public async Task Throws_with_descriptive_message_when_verify_is_false()
    {
        Func<Task> act = () => SilentSkipGuard.RunAsync(
            "create table evo_foo",
            mutate: () => Task.CompletedTask,
            verify: () => Task.FromResult(false));

        var ex = (await act.Should().ThrowAsync<SchemaMutationDidNotApplyException>()).Which;
        ex.Description.Should().Be("create table evo_foo");
        ex.Message.Should().Contain("create table evo_foo")
            .And.Contain("not present on re-read");
    }

    [Fact]
    public async Task Mutate_exceptions_propagate_without_invoking_verify()
    {
        var verified = false;
        Func<Task> act = () => SilentSkipGuard.RunAsync(
            "x",
            mutate: () => throw new InvalidOperationException("kaboom"),
            verify: () => { verified = true; return Task.FromResult(true); });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("kaboom");
        verified.Should().BeFalse();
    }
}
