using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization.Yaml;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace WoW.Two.Sdk.Beta.Be.Foundation.Tests.Serialization;

/// <summary>Reads hand-written YAML and refuses with the line and the field, never quoting a value.</summary>
public sealed class YamlReaderTests
{
    private const string Descriptor = """
        descriptor: 1
        product: forever-pin
        tier: paid
        empty: ~
        services:
          web:
            image: ghcr.io/o/web
            ports: [8080]
        """;

    [Fact]
    public void Load_ShouldReadTheMapping_AndItsValues()
    {
        var root = YamlReader.Mapping(YamlReader.Load(Encoding.UTF8.GetBytes(Descriptor), "deploy.yml"), "deploy.yml");

        YamlReader.Text(YamlReader.Required(root, "product", "product"), "product").Should().Be("forever-pin");
        YamlReader.Matching(YamlReader.Required(root, "product", "product"), "product", "^[a-z][a-z0-9-]*$", "a slug").Should().Be("forever-pin");
        YamlReader.Word<Tier>(YamlReader.Required(root, "tier", "tier"), "tier").Should().Be(Tier.Paid);
        var services = YamlReader.Mapping(YamlReader.Required(root, "services", "services"), "services");
        var web = YamlReader.Mapping(YamlReader.Required(services, "web", "services.web"), "services.web");
        YamlReader.Sequence(YamlReader.Required(web, "ports", "services.web.ports"), "services.web.ports").Children.Should().ContainSingle();
    }

    [Fact]
    public void Get_ShouldReadAnEmptyValueAsAbsent()
    {
        var root = YamlReader.Mapping(YamlReader.Load(Descriptor, "deploy.yml"), "deploy.yml");

        YamlReader.Get(root, "empty").Should().BeNull();
        YamlReader.Get(root, "missing").Should().BeNull();
        var refusal = () => YamlReader.Required(root, "empty", "empty");
        refusal.Should().Throw<YamlRefusalException>().WithMessage("line 1: empty is required.");
    }

    [Theory]
    [InlineData("a: [1, 2", "line *: deploy.yml is not valid YAML.")]
    [InlineData("a: 1\n---\nb: 2\n", "line 1: deploy.yml must hold exactly one YAML document.")]
    [InlineData("", "line 1: deploy.yml must hold exactly one YAML document.")]
    public void Load_ShouldRefuseAFileThatIsNotOneDocument(string text, string message)
    {
        var refusal = () => YamlReader.Load(text, "deploy.yml");

        refusal.Should().Throw<YamlRefusalException>().WithMessage(message);
    }

    [Fact]
    public void Checks_ShouldNameTheLineAndTheField_WithoutQuotingTheValue()
    {
        var root = YamlReader.Mapping(YamlReader.Load(Descriptor, "deploy.yml"), "deploy.yml");

        var slug = () => YamlReader.Matching(YamlReader.Required(root, "tier", "tier"), "tier", "^[0-9]+$", "a number");
        var word = () => YamlReader.Word<Tier>(YamlReader.Required(root, "product", "product"), "product");
        var list = () => YamlReader.Sequence(YamlReader.Required(root, "services", "services"), "services");
        var text = () => YamlReader.Text(YamlReader.Required(root, "services", "services"), "services");

        slug.Should().Throw<YamlRefusalException>().WithMessage("line 3: tier must be a number.");
        word.Should().Throw<YamlRefusalException>().WithMessage("line 2: product must be free, team or paid.");
        list.Should().Throw<YamlRefusalException>().WithMessage("line 6: services must be a list.");
        text.Should().Throw<YamlRefusalException>().WithMessage("line 6: services must be one line of text.");
    }

    [Fact]
    public void Word_ShouldNameTheOnlyWord_ForAOneMemberEnum()
    {
        var node = YamlReader.Load("other", "flag.yml");

        var refusal = () => YamlReader.Word<Single>(node, "flag");

        refusal.Should().Throw<YamlRefusalException>().WithMessage("line 1: flag must be only.");
    }

    [Fact]
    public void Text_ShouldRefuseSeveralLines()
    {
        var node = YamlReader.Load("description: |\n  one\n  two\n", "deploy.yml");
        var value = YamlReader.Required(YamlReader.Mapping(node, "deploy.yml"), "description", "description");

        var refusal = () => YamlReader.Text(value, "description");

        refusal.Should().Throw<YamlRefusalException>().WithMessage("line 1: description must be one line of text.");
    }

    [Fact]
    public void Short_ShouldCapAQuotedKey()
    {
        YamlReader.Short("web").Should().Be("web");
        YamlReader.Short(new string('k', 60)).Should().Be(new string('k', YamlReader.MaxQuotedKeyLength) + "…");
    }

    [Fact]
    public void Refuse_ShouldPointAtTheNode()
    {
        var root = YamlReader.Mapping(YamlReader.Load(Descriptor, "deploy.yml"), "deploy.yml");
        YamlNode services = YamlReader.Required(root, "services", "services");

        YamlReader.Refuse(services, "services", "must name one service").Message.Should().Be("line 6: services must name one service.");
    }

    private enum Tier
    {
        Free,
        Team,
        Paid,
    }

    private enum Single
    {
        Only,
    }
}
