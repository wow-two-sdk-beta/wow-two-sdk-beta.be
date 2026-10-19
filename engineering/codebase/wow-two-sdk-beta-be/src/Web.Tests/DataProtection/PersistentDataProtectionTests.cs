using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Web.DataProtection;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Web.Tests.DataProtection;

/// <summary>A persistent key ring lets a replacement host read payloads protected before it started.</summary>
public sealed class PersistentDataProtectionTests : IDisposable
{
    private readonly string _keys = Path.Combine(Path.GetTempPath(), "wow2-keys", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProtectedPayload_SurvivesAHostReplacement()
    {
        string token;
        using (var first = Build(Environments.Production, _keys))
        {
            token = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("guest").Protect("owner-1");
        }

        using var replacement = Build(Environments.Production, _keys);

        replacement.GetRequiredService<IDataProtectionProvider>().CreateProtector("guest").Unprotect(token).Should().Be("owner-1");
        Directory.GetFiles(_keys, "*.xml").Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Development", "dev", false)]
    [InlineData("Production", "prod", false)]
    [InlineData("Production", null, false)]
    public void KeyDirectory_IsRequiredOutsideALocalRun(string environment, string? deployEnvironment, bool relaxed)
    {
        using var provider = Build(environment, keyDirectory: null, deployEnvironment);

        var resolve = () => provider.GetRequiredService<PersistentDataProtectionOptions>();

        if (relaxed)
            resolve().KeyDirectory.Should().BeNull(); // a local run may use an ephemeral key ring
        else
            resolve.Should().Throw<OptionsValidationException>(); // deployed dev restarts too, and ephemeral keys void its cookies
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_keys, recursive: true);
        }
        catch (IOException)
        {
            // A missing or leaked temp directory must not fail a test.
        }
    }

    private static ServiceProvider Build(string environment, string? keyDirectory, string? deployEnvironment = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment })
            .Build());
        services.AddPersistentDataProtection(options =>
        {
            options.ApplicationName = "WoW2.Tests";
            options.KeyDirectory = keyDirectory;
        });
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "WoW2.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
