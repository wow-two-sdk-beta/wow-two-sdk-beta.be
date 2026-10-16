using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Naming;

namespace WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Postgres;

/// <summary>Npgsql name translator that routes type and member names through the SDK casing engine, so enum labels match the rest of the SDK's casing instead of Npgsql's built-in snake translator.</summary>
/// <param name="style">The casing applied to translated names.</param>
public sealed class CaseStyleNameTranslator(CaseStyle style) : INpgsqlNameTranslator
{
    /// <inheritdoc />
    public string TranslateTypeName(string clrName) => CaseMapper.ToCase(clrName, style);

    /// <inheritdoc />
    public string TranslateMemberName(string clrName) => CaseMapper.ToCase(clrName, style);
}
