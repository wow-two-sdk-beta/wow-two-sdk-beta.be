using Dapper;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Cli;

// Run the web-free migrator CLI: the shared migrate verb tree plus the authoring verbs.

DefaultTypeMap.MatchNamesWithUnderscores = true; // Dapper snake_case → PascalCase for migration_history reads

try
{
    var (root, tree) = new CliCommandBuilder(Console.Out, Console.Error).Build();
    return await tree.InvokeAsync(root.Parse(args), handleTermination: true, CancellationToken.None);
}
// The migrate verbs classify their own failures; this maps what escapes an authoring verb: 1 a missing file or folder, 2 anything else.
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"✗ {ex.Message}");
    return ex switch
    {
        DirectoryNotFoundException or FileNotFoundException => 1,
        _ => 2,
    };
}
