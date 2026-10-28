namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Verbs;

/// <summary>Reports a migrate run that broke the image contract: a missing or mismatched result line, a wrong exit, a rerun that changed something, a <c>check</c> that wrote.</summary>
public sealed class MigrateVerbAssertionException : Exception
{
    /// <summary>Creates the exception with no message.</summary>
    public MigrateVerbAssertionException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What broke, with the run's output.</param>
    public MigrateVerbAssertionException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception behind it.</summary>
    /// <param name="message">What broke.</param>
    /// <param name="innerException">The exception behind it.</param>
    public MigrateVerbAssertionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
