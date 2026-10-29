# Yaml

> `YamlReader` reads a hand-written YAML file — a manifest, a catalog, a descriptor — through the
> [YamlDotNet](https://github.com/aaubry/YamlDotNet) representation model, refusing with the line and the field.

```csharp
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization.Yaml;

try
{
    var root = YamlReader.Mapping(YamlReader.Load(bytes, "deploy.yml"), "deploy.yml");
    var name = YamlReader.Text(YamlReader.Required(root, "name", "name"), "name");
    var slug = YamlReader.Matching(YamlReader.Required(root, "product", "product"), "product", "^[a-z][a-z0-9-]*$", "a lowercase slug");
    var tier = YamlReader.Word<Tier>(YamlReader.Required(root, "tier", "tier"), "tier");      // "free" or "paid"
    foreach (var (key, value) in YamlReader.Mapping(YamlReader.Required(root, "services", "services"), "services").Children)
        Read(YamlReader.Mapping(value, "services." + YamlReader.Short(key.ToString())));
}
catch (YamlRefusalException refusal)
{
    return Problem(refusal.Message);   // "line 7: services.web.image is required."
}
```

- one document per file; no document, several, or invalid YAML refuse at the line YamlDotNet names
- a missing key and an empty one (`~`, `null`, blank) read alike as absent; unknown keys are left alone
- a refusal reads `line {n}: {field} {rule}.` and quotes no value; `Short` caps a quoted key at 40 characters
- `Matching` anchors nothing for you and gives a pattern one second per value; `Word<TEnum>` lists the lowercase names
- typed binding to classes stays with `YamlDotNet.Serialization`; this reader is for files people write by hand
