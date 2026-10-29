namespace WoW.Two.Sdk.Beta.Be.Foundation.Serialization.Yaml;

/// <summary>Represents a refused YAML file; its message names the line and the field and is safe to show the file's author.</summary>
public sealed class YamlRefusalException : Exception
{
    /// <summary>Initializes a refusal with no message.</summary>
    public YamlRefusalException()
    {
    }

    /// <summary>Initializes a refusal.</summary>
    /// <param name="message">The refusal: <c>line {n}: {field} {rule}.</c></param>
    public YamlRefusalException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a refusal with its cause.</summary>
    /// <param name="message">The refusal.</param>
    /// <param name="innerException">The cause.</param>
    public YamlRefusalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
