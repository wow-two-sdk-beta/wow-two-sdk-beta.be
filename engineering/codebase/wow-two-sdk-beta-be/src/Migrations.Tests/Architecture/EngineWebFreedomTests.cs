using AwesomeAssertions;
using NetArchTest.Rules;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;

namespace WoW.Two.Sdk.Beta.Be.Migrations.Tests.Architecture;

/// <summary>
/// Web-freedom guard: the bespoke migrator engine must stay runnable from any host (web startup, the web-free
/// <c>wow-migrate</c> CLI, a dev hosted service, tests), so its types may not depend on web / host / CLI
/// frameworks — even though the mono-lib assembly references ASP.NET for its other concerns.
/// </summary>
public sealed class EngineWebFreedomTests
{
    [Fact]
    public void BespokeEngine_ShouldNotDependOnWebHostOrCliFramework()
    {
        var result = Types.InAssembly(typeof(MigrationRunnerService).Assembly)
            .That().ResideInNamespace("WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "MediatR",
                "Microsoft.Extensions.Hosting",
                "System.CommandLine")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the migrator engine must stay host-agnostic; offending types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void VerbTree_ShouldNotDependOnWebHostEfOrDbUp()
    {
        // wow-migrate compiles the same tree, so it may use System.CommandLine and nothing a console tool lacks.
        var result = Types.InAssembly(typeof(MigrationRunnerService).Assembly)
            .That().ResideInNamespace("WoW.Two.Sdk.Beta.Be.Data.Migrations.Commands")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.Hosting",
                "Microsoft.EntityFrameworkCore",
                "DbUp")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the verb tree must stay web-free; offending types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
