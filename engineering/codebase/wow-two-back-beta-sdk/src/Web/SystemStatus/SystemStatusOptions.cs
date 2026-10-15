using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Backend.Beta.Web.SystemStatus;

/// <summary>Holds what the system status endpoint reports beside the build version.</summary>
public sealed record SystemStatusOptions
{
    /// <summary>Gets or sets the service name reported as <c>service</c>; <see langword="null"/> uses the version assembly's name.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Gets or sets the assembly the build version is read from; <see langword="null"/> uses the entry assembly.</summary>
    public Assembly? VersionAssembly { get; set; }

    /// <summary>Gets or sets a per-request source of app-specific facts added to the payload, such as a seal state; keys must not repeat the built-in ones.</summary>
    public Func<HttpContext, IEnumerable<KeyValuePair<string, object?>>>? Facts { get; set; }
}
