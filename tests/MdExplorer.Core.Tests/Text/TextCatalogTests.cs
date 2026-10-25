using System.Globalization;
using System.Resources;
using MdExplorer.Core.Text;

namespace MdExplorer.Core.Tests.Text;

/// <summary>
/// Der Katalog liefert Anzeigetexte aus den Ressourcen. Ein fehlender Schlüssel darf kein
/// Fenster am Öffnen hindern, muss in der Oberfläche aber sichtbar auffallen.
/// </summary>
public sealed class TextCatalogTests
{
    [Fact]
    public void Get_WithAKnownKey_ReturnsTheText()
    {
        TextCatalog sut = new(new FakeResources(new() { ["Action_Close"] = "Schließen" }));

        Assert.Equal("Schließen", sut.Get("Action_Close"), StringComparer.Ordinal);
    }

    [Fact]
    public void Get_WithAnUnknownKey_ReturnsTheMarkedKey()
    {
        TextCatalog sut = new(new FakeResources([]));

        Assert.Equal("[?Missing_Key?]", sut.Get("Missing_Key"), StringComparer.Ordinal);
    }

    [Fact]
    public void Format_InsertsTheValuesIntoThePlaceholders()
    {
        TextCatalog sut = new(new FakeResources(new() { ["Status_Loaded"] = "{0} von {1} geladen." }));

        Assert.Equal("3 von 7 geladen.", sut.Format("Status_Loaded", 3, 7), StringComparer.Ordinal);
    }

    [Fact]
    public void Get_WithAnEmptyKey_Throws()
    {
        TextCatalog sut = new(new FakeResources([]));

        _ = Assert.Throws<ArgumentException>(() => sut.Get(string.Empty));
    }

    // Ersetzt die eingebettete Ressourcendatei durch ein Wörterbuch.
    private sealed class FakeResources(Dictionary<string, string> texts) : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) =>
            texts.TryGetValue(name, out string? text) ? text : null;
    }
}
