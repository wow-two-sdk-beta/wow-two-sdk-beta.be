namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

/// <summary>Reports the findings of a lint run that must come back clean.</summary>
public sealed class MigrationLintException : Exception
{
    /// <summary>Creates the exception with no message.</summary>
    public MigrationLintException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The findings, one per line.</param>
    public MigrationLintException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception behind it.</summary>
    /// <param name="message">The findings.</param>
    /// <param name="innerException">The exception behind it.</param>
    public MigrationLintException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
