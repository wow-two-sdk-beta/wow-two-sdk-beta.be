namespace WoW.Two.Sdk.Beta.Be.Meta;

/// <summary>One verb run: the verb's own arguments and the streams it writes.</summary>
public sealed class ServiceVerbInvocation
{
    internal ServiceVerbInvocation(string name, string[] arguments, TextWriter output, TextWriter error)
    {
        Name = name;
        Arguments = arguments;
        Output = output;
        Error = error;
    }

    /// <summary>Gets the verb's name: the first process argument.</summary>
    public string Name { get; }

    /// <summary>Gets the arguments after the verb's name, such as <c>generate --role signing</c> for <c>keys</c>.</summary>
    public string[] Arguments { get; }

    /// <summary>Gets standard output: the verb's payload and result line only.</summary>
    public TextWriter Output { get; }

    /// <summary>Gets standard error: the human report and every log line.</summary>
    public TextWriter Error { get; }
}
