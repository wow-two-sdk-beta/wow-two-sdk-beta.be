namespace WoW.Two.Sdk.Beta.Be.Data.Sessions.Validators;

/// <summary>Validates scoped resolution even when the container disables scope validation.</summary>
internal sealed class DataSessionScopeValidator(IServiceProvider root)
{
    internal void Validate(IServiceProvider provider)
    {
        if (ReferenceEquals(root, provider))
        {
            throw new InvalidOperationException("Resolve IDataSession from an explicit DI scope, never the root provider.");
        }
    }
}
