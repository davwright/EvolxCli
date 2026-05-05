namespace Evolx.Cli.Dataverse;

/// <summary>
/// Some Dataverse metadata mutations return 200/204 even when the change wasn't applied
/// (most often: duplicate SchemaName on table/column/choice creates). The PowerShell module
/// historically logged "successful" in those cases. To prevent that class of bug we re-read
/// after every mutation and confirm the post-state matches expectations.
/// </summary>
internal static class SilentSkipGuard
{
    /// <summary>
    /// Run a mutation, then verify it actually landed. Throws
    /// <see cref="SchemaMutationDidNotApplyException"/> if the verifier returns false.
    /// </summary>
    /// <param name="description">Human-readable description used in the failure message ("create table evo_foo").</param>
    /// <param name="mutate">The actual mutation HTTP call.</param>
    /// <param name="verify">Re-read predicate. Returns true when the post-state contains the change.</param>
    public static async Task RunAsync(string description, Func<Task> mutate, Func<Task<bool>> verify)
    {
        await mutate();
        var applied = await verify();
        if (!applied)
            throw new SchemaMutationDidNotApplyException(description);
    }
}

/// <summary>
/// Thrown when Dataverse returned a successful HTTP status to a metadata write but the
/// re-read does not show the change. Surfaces the silent-skip-on-duplicate behaviour.
/// </summary>
public sealed class SchemaMutationDidNotApplyException : Exception
{
    public SchemaMutationDidNotApplyException(string description)
        : base($"Dataverse accepted the request but the change ({description}) is not present on re-read. " +
               "This usually means a duplicate SchemaName or a partial-update body that was silently ignored.")
    {
        Description = description;
    }

    public string Description { get; }
}
