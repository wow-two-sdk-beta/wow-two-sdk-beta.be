namespace WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations;

/// <summary>Reports a set whose <c>Apply.sql</c> or <c>Rollback.sql</c> fails or changes the database when it runs again (M59).</summary>
public sealed class MigrationIdempotencyException : Exception
{
    /// <summary>Creates the exception with no message.</summary>
    public MigrationIdempotencyException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The failed step.</param>
    public MigrationIdempotencyException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception behind it.</summary>
    /// <param name="message">The failed step.</param>
    /// <param name="innerException">The exception behind it.</param>
    public MigrationIdempotencyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
