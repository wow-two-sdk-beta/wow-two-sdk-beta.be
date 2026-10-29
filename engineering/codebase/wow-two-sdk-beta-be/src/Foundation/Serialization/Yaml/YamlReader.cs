using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Serialization.Yaml;

/// <summary>
/// Reads a hand-written YAML file — a manifest, a catalog, a descriptor — through the YamlDotNet representation model,
/// so every refusal names the line and the field. Unknown keys are left alone, and a refusal never quotes the file
/// beyond a shortened key.
/// </summary>
/// <remarks>
///   - <see cref="Load(byte[], string)"/> takes exactly one document; every other call reads a node it returned
///   - each check throws a <see cref="YamlRefusalException"/>: <c>line {n}: {field} {rule}.</c>, safe to show the file's author
///   - a missing key and a key with no value (<c>~</c>, <c>null</c>, empty) read alike, as absent
/// </remarks>
/// <example>
/// <code>
/// var root = YamlReader.Mapping(YamlReader.Load(bytes, "deploy.yml"), "deploy.yml");
/// var name = YamlReader.Text(YamlReader.Required(root, "name", "name"), "name");
/// var tier = YamlReader.Word&lt;Tier&gt;(YamlReader.Required(root, "tier", "tier"), "tier");   // "free" or "paid"
/// </code>
/// </example>
public static class YamlReader
{
    /// <summary>The longest key a refusal quotes before it shortens the key.</summary>
    public const int MaxQuotedKeyLength = 40;

    /// <summary>The longest a pattern check may run on one value.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Loads the single document of a YAML file.</summary>
    /// <param name="content">The file's bytes, UTF-8.</param>
    /// <param name="file">The file's name, for refusals.</param>
    /// <returns>The document's root node.</returns>
    /// <exception cref="YamlRefusalException">The bytes are not YAML, or hold no document or several.</exception>
    public static YamlNode Load(byte[] content, string file)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Load(Encoding.UTF8.GetString(content), file);
    }

    /// <summary>Loads the single document of a YAML text.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="file">The file's name, for refusals.</param>
    /// <returns>The document's root node.</returns>
    /// <exception cref="YamlRefusalException">The text is not YAML, or holds no document or several.</exception>
    public static YamlNode Load(string text, string file)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(text);
            stream.Load(reader);
        }
        catch (YamlException exception)
        {
            throw new YamlRefusalException($"line {exception.Start.Line}: {file} is not valid YAML.");
        }

        if (stream.Documents.Count != 1)
            throw new YamlRefusalException($"line 1: {file} must hold exactly one YAML document.");
        return stream.Documents[0].RootNode;
    }

    /// <summary>Requires a mapping.</summary>
    /// <param name="node">The node.</param>
    /// <param name="field">The field's name, for the refusal.</param>
    /// <returns>The mapping.</returns>
    public static YamlMappingNode Mapping(YamlNode node, string field) =>
        node as YamlMappingNode ?? throw Refuse(node, field, "must be a mapping");

    /// <summary>Requires a sequence.</summary>
    /// <param name="node">The node.</param>
    /// <param name="field">The field's name, for the refusal.</param>
    /// <returns>The sequence.</returns>
    public static YamlSequenceNode Sequence(YamlNode node, string field) =>
        node as YamlSequenceNode ?? throw Refuse(node, field, "must be a list");

    /// <summary>Gets a key's value, or <see langword="null"/> when the mapping lacks the key or gives it no value.</summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static YamlNode? Get(YamlMappingNode mapping, string key)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        foreach (var (name, value) in mapping.Children)
        {
            if (name is YamlScalarNode { Value: { } text } && text == key)
                return IsNull(value) ? null : value;
        }

        return null;
    }

    /// <summary>Requires a key whose value is present.</summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="key">The key.</param>
    /// <param name="field">The field's name, for the refusal, such as <c>services.web.image</c>.</param>
    /// <returns>The value.</returns>
    public static YamlNode Required(YamlMappingNode mapping, string key, string field) =>
        Get(mapping, key) ?? throw Refuse(mapping, field, "is required");

    /// <summary>Requires one line of text, trimmed.</summary>
    /// <param name="node">The node.</param>
    /// <param name="field">The field's name, for the refusal.</param>
    /// <returns>The text, trimmed.</returns>
    public static string Text(YamlNode node, string field)
    {
        var text = node is YamlScalarNode { Value: { } value } ? value.Trim() : null;
        if (string.IsNullOrEmpty(text) || text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal))
            throw Refuse(node, field, "must be one line of text");
        return text;
    }

    /// <summary>Requires a value that matches a pattern.</summary>
    /// <param name="node">The node.</param>
    /// <param name="field">The field's name, for the refusal.</param>
    /// <param name="pattern">The pattern the whole value must match; anchor it.</param>
    /// <param name="shape">What a matching value looks like, for the refusal: <c>a lowercase slug</c>.</param>
    /// <returns>The value, as written.</returns>
    public static string Matching(YamlNode node, string field, string pattern, string shape)
    {
        var text = node is YamlScalarNode { Value: { } value } ? value : null;
        if (text is null || !Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, MatchTimeout))
            throw Refuse(node, field, "must be " + shape);
        return text;
    }

    /// <summary>Requires one of an enum's members, written as a lowercase word, and returns the member.</summary>
    /// <typeparam name="TEnum">The enum whose member names, lowercased, are the words.</typeparam>
    /// <param name="node">The node.</param>
    /// <param name="field">The field's name, for the refusal, which lists the words.</param>
    /// <returns>The member.</returns>
    public static TEnum Word<TEnum>(YamlNode node, string field) where TEnum : struct, Enum
    {
        var words = Enum.GetNames<TEnum>().Select(name => name.ToLowerInvariant()).ToArray();
        var text = node is YamlScalarNode { Value: { } value } ? value : null;
        if (text is null || !words.Contains(text, StringComparer.Ordinal))
            throw Refuse(node, field, "must be " + (words.Length == 1 ? words[0] : string.Join(", ", words[..^1]) + " or " + words[^1]));
        return Enum.Parse<TEnum>(text, ignoreCase: true);
    }

    /// <summary>Builds a refusal at a node: <c>line {n}: {field} {rule}.</c></summary>
    /// <param name="node">The node the refusal points at.</param>
    /// <param name="field">The field's name.</param>
    /// <param name="rule">What the field must be or is: <c>must be a list</c>, <c>is required</c>.</param>
    /// <returns>The refusal, for the caller to throw.</returns>
    public static YamlRefusalException Refuse(YamlNode node, string field, string rule)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new YamlRefusalException($"line {node.Start.Line}: {field} {rule}.");
    }

    /// <summary>Shortens a key for a refusal, so a refusal never carries much of the file.</summary>
    /// <param name="key">The key as written.</param>
    /// <returns>The key, cut at <see cref="MaxQuotedKeyLength"/> characters with an ellipsis.</returns>
    public static string Short(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Length <= MaxQuotedKeyLength ? key : key[..MaxQuotedKeyLength] + "…";
    }

    private static bool IsNull(YamlNode node) =>
        node is YamlScalarNode { Style: ScalarStyle.Plain, Value: null or "" or "~" or "null" or "Null" or "NULL" };
}
