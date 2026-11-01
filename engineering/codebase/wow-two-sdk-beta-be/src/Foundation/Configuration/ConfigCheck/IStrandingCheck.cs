using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;

/// <summary>Counts the people the current settings leave no way to use the service, such as a removed sign-in method that is some people's only one.</summary>
/// <remarks>
///   - <c>config check</c> and every boot run each registered check; a count above zero refuses the change (exit <c>1</c>, a deployed boot refuses)
///   - read the database in one read-only transaction through the service's own connection string
///   - count rows, backed by an index the owner's migration set creates; never a scan per row, never a personal value read out
///   - finish in seconds; a failure (an unreachable database) ends the check with exit <c>2</c>
///   - register it with <see cref="ConfigCheckServiceCollectionExtensions.AddStrandingCheck{TCheck}"/> in the <c>Add{Domain}</c> that binds the setting
/// </remarks>
/// <example>
/// <code>
/// internal sealed class SignInMethodStrandingCheck(IdentityMethodSettings methods, NpgsqlDataSource database) : IStrandingCheck
/// {
///     public async Task&lt;IReadOnlyList&lt;StrandedCount&gt;&gt; CountAsync(CancellationToken ct)
///     {
///         if (methods.Password.IsEnabled)
///             return [];
///         await using var connection = await database.OpenConnectionAsync(ct);
///         await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
///         // SET TRANSACTION READ ONLY; select count(*) from users where only_method = 'password'
///         return [new StrandedCount { Code = "only-method-removed", Setting = "Identity:Methods:Password:IsEnabled", Count = count }];
///     }
/// }
/// </code>
/// </example>
public interface IStrandingCheck
{
    /// <summary>Counts, per rule, the people the current settings strand; a rule that strands nobody may return a zero count or nothing.</summary>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>One count per rule; counts of zero are dropped.</returns>
    Task<IReadOnlyList<StrandedCount>> CountAsync(CancellationToken ct);
}

/// <summary>One stranding rule's count: the rule, the setting whose change strands people, and how many.</summary>
public sealed record StrandedCount
{
    /// <summary>Gets the rule's short kebab-case id, such as <c>only-method-removed</c>.</summary>
    [JsonPropertyOrder(0)]
    public required string Code { get; init; }

    /// <summary>Gets the full setting path, such as <c>Identity:Methods:Password:IsEnabled</c>.</summary>
    [JsonPropertyOrder(1)]
    public required string Setting { get; init; }

    /// <summary>Gets how many people the change strands; never who.</summary>
    [JsonPropertyOrder(2)]
    public required long Count { get; init; }
}
