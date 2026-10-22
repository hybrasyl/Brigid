using System.Text.Json;
using Brigid.Data.AssetPacks;
using Xunit;

namespace Brigid.Tests;

// The registry once read an absent schema_version as 0 and an absent pack_id as "", and accepted both. Each manifest is
// deserialized with the same default options that the registry uses.
public class AssetPackManifestValidationTests
{
    private static string? Reject(string json)
        => AssetPackRegistry.GetManifestRejection(JsonSerializer.Deserialize<AssetPackManifest>(json)!);

    [Fact]
    public void GetManifestRejection_AcceptsAFullValidManifest()
        => Assert.Null(Reject("""{ "schema_version": 1, "pack_id": "p", "content_type": "item_icons" }"""));

    [Theory]
    [InlineData("""{ "pack_id": "p" }""", "has no schema_version")]
    [InlineData("""{ "schema_version": null, "pack_id": "p" }""", "has no schema_version")]
    [InlineData("""{ "schema_version": 0, "pack_id": "p" }""", "not a valid version")]
    [InlineData("""{ "schema_version": -7, "pack_id": "p" }""", "not a valid version")]
    [InlineData("""{ "schema_version": 2, "pack_id": "p" }""", "newer than supported")]
    [InlineData("""{ "schema_version": 1 }""", "has no pack_id")]
    [InlineData("""{ "schema_version": 1, "pack_id": "  " }""", "has no pack_id")]
    [InlineData("""{ "schema_version": 1, "pack_id": null }""", "has no pack_id")]
    [InlineData("""{ }""", "has no schema_version")]
    public void GetManifestRejection_RefusesAMissingOrInvalidRequiredField(string json, string reason)
        => Assert.Contains(reason, Reject(json));

    [Theory]
    [InlineData("""{ "schema_version": 1, "pack_id": "icy-steel", "content_type": "ui_theme", "name": "Icy Steel" }""", "Icy Steel")]
    [InlineData("""{ "schema_version": 1, "pack_id": "icy-steel", "content_type": "ui_theme", "name": "  Icy Steel  " }""", "Icy Steel")]
    [InlineData("""{ "schema_version": 1, "pack_id": "icy-steel", "content_type": "ui_theme", "name": "   " }""", "icy-steel")]
    [InlineData("""{ "schema_version": 1, "pack_id": "icy-steel", "content_type": "ui_theme" }""", "icy-steel")]
    public void TheDisplayNameIsTheName_ElseThePackId(string json, string expected)
        => Assert.Equal(expected, JsonSerializer.Deserialize<AssetPackManifest>(json)!.DisplayName);

    [Theory]
    [InlineData("none")]
    [InlineData(" NONE ")]
    public void ThePackIdNoneIsReserved(string packId)
        => Assert.Contains("which is reserved", Reject($$"""{ "schema_version": 1, "pack_id": "{{packId}}", "content_type": "ui_theme" }"""));

    //the settings file keeps the selected theme id on one trimmed line, so an id with a space or a control character
    //would come back different after a restart (PR #64 review)
    [Theory]
    [InlineData("stone ")]
    [InlineData(" stone")]
    [InlineData("icy steel")]
    [InlineData("stone\\n")]
    [InlineData("stone\\r\\nUiTheme: wood")]
    [InlineData("stone\\t")]
    [InlineData("st:one")]
    [InlineData("st\\u00f6ne")]
    public void AThemePackIdTakesOnlyLettersDigitsHyphensAndUnderscores(string packId)
        => Assert.Contains("which a ui_theme cannot have", Reject($$"""{ "schema_version": 1, "pack_id": "{{packId}}", "content_type": "ui_theme" }"""));

    [Fact]
    public void TheThemeIdRuleIsOnlyForThemes()
        => Assert.Null(Reject("""{ "schema_version": 1, "pack_id": "my items", "content_type": "item_icons" }"""));
}
