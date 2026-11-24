using System.Text.Json.Serialization;

namespace MdExplorer.App.Services;

/// <summary>Geometrie eines persistierten Fensters in geräteunabhängigen Einheiten (1/96 Zoll), wie WPF sie führt.</summary>
/// <param name="Left">Linke Kante.</param>
/// <param name="Top">Obere Kante.</param>
/// <param name="Width">Breite.</param>
/// <param name="Height">Höhe.</param>
internal sealed record WindowGeometry(double Left, double Top, double Width, double Height)
{
    /// <summary>Ob sich ein Fenster mit dieser Geometrie aufbauen lässt.</summary>
    [JsonIgnore]
    public bool IsUsable =>
        double.IsFinite(Left) && double.IsFinite(Top) && UiLayout.IsUsableLength(Width) && UiLayout.IsUsableLength(Height);
}
