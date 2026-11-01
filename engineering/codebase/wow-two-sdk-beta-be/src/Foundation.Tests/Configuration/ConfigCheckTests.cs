using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration.ConfigCheck;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Configuration;

/// <summary>
/// Covers <see cref="ConfigCheckCommand"/>: the boot's own settings validation without serving — invalid values, unknown keys
/// and stranding in one run, one <c>config-result</c> line last on stdout, exits <c>0</c>, <c>1</c> and <c>2</c>.
/// </summary>
public sealed class ConfigCheckTests
{
    private static readonly Dictionary<string, string?> ValidShop = new()
    {
        ["Shop:Name"] = "corner",
        ["Shop:MinItems"] = "1",
        ["Shop:MaxItems"] = "10",
        ["Logging:LogLevel:Default"] = "Information",
    };

    [Fact]
    public async Task Valid_ShouldExitZero_WithEmptyFindingsOnTheLastLine()
    {
        var (exit, output, error) = await RunAsync(ValidShop, services => services.AddShop(ValidShop));

        exit.Should().Be(0, error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle().Which.Should().StartWith(ConfigCheckResult.ResultLinePrefix);
        using var json = JsonDocument.Parse(lines[0][ConfigCheckResult.ResultLinePrefix.Length..]);
        json.RootElement.GetProperty("verb").GetString().Should().Be("check");
        json.RootElement.GetProperty("exit").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("service").GetString().Should().Be("shop");
        json.RootElement.GetProperty("invalid").GetArrayLength().Should().Be(0);
        json.RootElement.GetProperty("unknown").GetArrayLength().Should().Be(0);
        json.RootElement.GetProperty("stranded").GetArrayLength().Should().Be(0, "no stranding check is registered");
        json.RootElement.TryGetProperty("error", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("durationMs", out _).Should().BeTrue();
        error.Should().Contain("config check: consistent");
    }

    [Fact]
    public async Task InvalidValues_ShouldExitOne_WithEveryFindingOfEveryRecordInOneRun()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Shop:MinItems"] = "20",
            ["Shop:MaxItems"] = "16",
            ["Shop:Shipping:Days"] = "0",
        };

        var (exit, output, error) = await RunAsync(settings, services =>
        {
            services.AddShop(settings);
            services.AddValidatedSettings<ShippingSettings>(Configuration(settings), "Shop:Shipping",
                rules => rules.Validate(shipping => shipping.Days > 0, "Shipping takes at least a day."));
        });

        exit.Should().Be(1, error);
        var result = ConfigCheckResult.FromOutput(output)!;
        result.Exit.Should().Be(1);
        result.Invalid.Should().BeEquivalentTo(
        [
            new ConfigCheckInvalid { Code = "required", Setting = "Shop:Name", Message = "The Name field is required." },
            new ConfigCheckInvalid { Code = "range", Setting = "Shop:MinItems", Message = "20 exceeds MaxItems 16" },
            new ConfigCheckInvalid { Code = "invalid", Setting = "Shop:Shipping", Message = "Shipping takes at least a day." },
        ]);
        result.Unknown.Should().BeEmpty();
        error.Should().Contain("invalid  Shop:Name [required]");
    }

    [Fact]
    public async Task DataAnnotationRange_ShouldNameTheAttribute()
    {
        var settings = new Dictionary<string, string?>(ValidShop) { ["Shop:MaxItems"] = "500", ["Shop:MinItems"] = "1" };

        var (exit, output, _) = await RunAsync(settings, services => services.AddShop(settings));

        exit.Should().Be(1);
        ConfigCheckResult.FromOutput(output)!.Invalid.Should().ContainSingle()
            .Which.Should().Match<ConfigCheckInvalid>(invalid => invalid.Code == "range" && invalid.Setting == "Shop:MaxItems");
    }

    [Fact]
    public async Task UnconvertibleValue_ShouldReportTheKey_NeverTheValue()
    {
        var settings = new Dictionary<string, string?>(ValidShop) { ["Shop:MaxItems"] = "plenty-of-secret" };

        var (exit, output, error) = await RunAsync(settings, services => services.AddShop(settings));

        exit.Should().Be(1, error);
        ConfigCheckResult.FromOutput(output)!.Invalid.Should().ContainSingle()
            .Which.Should().Match<ConfigCheckInvalid>(invalid => invalid.Code == "type" && invalid.Setting == "Shop:MaxItems");
        (output + error).Should().NotContain("plenty-of-secret");
    }

    [Fact]
    public async Task UnknownKeys_ShouldBeFoundUnderBoundSectionsAndDeclaredRoots_ButNotUnderOtherSections()
    {
        var settings = new Dictionary<string, string?>(ValidShop)
        {
            ["Shop:MaxItemz"] = "10",
            ["Shop:Tags:0"] = "fresh",
            ["Shop:Prices:basic"] = "5",
            ["Shop:Shipping:Days"] = "2",
            ["Shop:Shipping:Carriers:0:Name"] = "post",
            ["Shop:Shipping:Carriers:0:Nmae"] = "typo",
            ["Shop:Module:IsEnabled"] = "true",
            ["Shop:Module:IsEnabeld"] = "true",
            ["Shop:SignUp:Enabled"] = "true",
            ["Payments:Provider"] = "stripe",
        };

        var (exit, output, error) = await RunAsync(settings, services =>
        {
            services.AddShop(settings);
            services.AddValidatedSettings<ShippingSettings>(Configuration(settings), "Shop:Shipping", _ => { });
            services.AddModuleOptions<ShopModuleOptions>("Shop:Module", configure: null);
            services.AddSettingsRoot("Shop");
        });

        exit.Should().Be(1, error);
        ConfigCheckResult.FromOutput(output)!.Unknown.Should().Equal(
            "Shop:MaxItemz", "Shop:Module:IsEnabeld", "Shop:Shipping:Carriers:0:Nmae", "Shop:SignUp:Enabled");
        output.Should().NotContain("typo");
    }

    [Fact]
    public async Task UnknownKeys_InAndBesideAModuleSection_ShouldBeFoundOnlyUnderADeclaredRoot()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Shop:Module:IsEnabled"] = "true",
            ["Shop:Module:IsEnabeld"] = "true",
            ["Shop:SignUp:Enabled"] = "true",
        };

        var withoutRoot = await RunAsync(settings, services => services.AddModuleOptions<ShopModuleOptions>("Shop:Module", configure: null));
        var withRoot = await RunAsync(settings, services => services
            .AddModuleOptions<ShopModuleOptions>("Shop:Module", configure: null)
            .AddSettingsRoot("Shop"));

        withoutRoot.Exit.Should().Be(0, "a module's section belongs to the service only under a root it declares");
        withRoot.Exit.Should().Be(1, withRoot.Error);
        ConfigCheckResult.FromOutput(withRoot.Output)!.Unknown.Should().Equal("Shop:Module:IsEnabeld", "Shop:SignUp:Enabled");
    }

    [Fact]
    public async Task Stranding_ShouldExitOne_WithEachCountAboveZero()
    {
        var (exit, output, error) = await RunAsync(ValidShop, services => services
            .AddShop(ValidShop)
            .AddStrandingCheck<FixedStrandingCheck>()
            .AddSingleton(new StrandingCounts
            {
                Counts =
                [
                    new StrandedCount { Code = "only-method-removed", Setting = "Shop:Methods:Password:IsEnabled", Count = 3 },
                    new StrandedCount { Code = "nobody", Setting = "Shop:Methods:Email:IsEnabled", Count = 0 },
                ],
            }));

        exit.Should().Be(1, error);
        ConfigCheckResult.FromOutput(output)!.Stranded.Should().Equal(
            new StrandedCount { Code = "only-method-removed", Setting = "Shop:Methods:Password:IsEnabled", Count = 3 });
        output.Should().Contain("\"stranded\":[{\"code\":\"only-method-removed\",\"setting\":\"Shop:Methods:Password:IsEnabled\",\"count\":3}]");
        error.Should().Contain("stranded Shop:Methods:Password:IsEnabled [only-method-removed]: 3");
    }

    [Fact]
    public async Task FailingStrandingCheck_ShouldExitTwo_WithAnErrorInPlaceOfTheFindings()
    {
        var (exit, output, error) = await RunAsync(ValidShop, services => services
            .AddShop(ValidShop)
            .AddStrandingCheck<UnreachableStrandingCheck>());

        exit.Should().Be(2, error);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1];
        using var json = JsonDocument.Parse(line[ConfigCheckResult.ResultLinePrefix.Length..]);
        json.RootElement.GetProperty("exit").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("error").GetString().Should().Contain(nameof(UnreachableStrandingCheck));
        json.RootElement.TryGetProperty("invalid", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("unknown", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("stranded", out _).Should().BeFalse();
        (output + error).Should().NotContain("hunter2", "a credential in an exception message is redacted");
        error.Should().Contain("connection refused");
    }

    [Theory]
    [InlineData("config")]
    [InlineData("config", "verify")]
    [InlineData("config", "check", "--json")]
    public async Task OtherArguments_ShouldExitTwo_WithTheUsage(params string[] args)
    {
        var (exit, output, error) = await RunAsync(ValidShop, services => services.AddShop(ValidShop), args);

        exit.Should().Be(2);
        ConfigCheckResult.FromOutput(output)!.Error.Should().Be(ConfigCheckCommand.Usage);
        error.Should().Contain(ConfigCheckCommand.Usage);
    }

    [Fact]
    public void ValidateSetting_ShouldRefuseAnUnreadableCode()
    {
        var builder = new ServiceCollection().AddOptions<ShopSettings>();

        var act = () => builder.ValidateSetting("MinItems", "Range Rule", _ => true, "never");

        act.Should().Throw<ArgumentException>().WithParameterName("code");
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(
        Dictionary<string, string?> settings, Action<IServiceCollection> register, params string[] args)
    {
        var services = new ServiceCollection().AddSingleton<IConfiguration>(Configuration(settings));
        register(services);
        await using var provider = services.BuildServiceProvider();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = await ConfigCheckCommand.RunAsync(provider, "shop", args.Length == 0 ? ["config", "check"] : args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}

/// <summary>A service's own settings, bound from <c>Shop</c>.</summary>
public sealed class ShopSettings
{
    /// <summary>Gets or sets the shop's name.</summary>
    [Required]
    public string? Name { get; set; }

    /// <summary>Gets or sets the fewest items an order takes.</summary>
    public int MinItems { get; set; }

    /// <summary>Gets or sets the most items an order takes.</summary>
    [Range(1, 100)]
    public int MaxItems { get; set; } = 10;

    /// <summary>Gets the tags, bound by index.</summary>
    public List<string> Tags { get; } = [];

    /// <summary>Gets the prices by plan, bound by key.</summary>
    public Dictionary<string, int> Prices { get; } = [];
}

/// <summary>Shipping settings, bound from <c>Shop:Shipping</c>.</summary>
public sealed class ShippingSettings
{
    /// <summary>Gets or sets the days a parcel takes.</summary>
    public int Days { get; set; } = 1;

    /// <summary>Gets the carriers, each an object.</summary>
    public List<Carrier> Carriers { get; } = [];
}

/// <summary>One carrier.</summary>
public sealed class Carrier
{
    /// <summary>Gets or sets the carrier's name.</summary>
    public string? Name { get; set; }
}

/// <summary>A module's options, bound from <c>Shop:Module</c>.</summary>
public sealed class ShopModuleOptions
{
    /// <summary>Gets or sets whether the module runs.</summary>
    public bool IsEnabled { get; set; }
}

/// <summary>The counts <see cref="FixedStrandingCheck"/> returns.</summary>
public sealed record StrandingCounts
{
    /// <summary>Gets the counts.</summary>
    public required IReadOnlyList<StrandedCount> Counts { get; init; }
}

/// <summary>Returns fixed counts.</summary>
internal sealed class FixedStrandingCheck(StrandingCounts counts) : IStrandingCheck
{
    public Task<IReadOnlyList<StrandedCount>> CountAsync(CancellationToken ct) => Task.FromResult(counts.Counts);
}

/// <summary>Fails as an unreachable database does.</summary>
internal sealed class UnreachableStrandingCheck : IStrandingCheck
{
    public Task<IReadOnlyList<StrandedCount>> CountAsync(CancellationToken ct) =>
        throw new InvalidOperationException("connection refused (Host=db;Password=hunter2)");
}

/// <summary>Registers <see cref="ShopSettings"/> as a service's own settings.</summary>
internal static class ShopRegistration
{
    public static IServiceCollection AddShop(this IServiceCollection services, Dictionary<string, string?> settings) =>
        services.AddValidatedSettings<ShopSettings>(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), "Shop",
            rules => rules.ValidateSetting(nameof(ShopSettings.MinItems), "range",
                shop => shop.MinItems <= shop.MaxItems,
                shop => $"{shop.MinItems} exceeds MaxItems {shop.MaxItems}"));
}
