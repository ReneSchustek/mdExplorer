using System.Globalization;
using System.Windows.Data;
using MdExplorer.App.Localization;
using MdExplorer.App.Services;
using MdExplorer.Core.Models;
using MdExplorer.Search.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Converters;

/// <summary>
/// Zeigt Werte der Aufzählungen, die in der Oberfläche stehen, mit ihrem Anzeigetext statt mit
/// dem Bezeichner aus dem Code.
/// </summary>
/// <remarks>
/// Jeder Schlüssel steht ausgeschrieben. Aus Typ und Wert zusammengesetzt, fände der Wächter der
/// Ressourcendatei keine Verwendung, und ein Tippfehler zeigte erst in der Oberfläche
/// <c>[?Schlüssel?]</c>. Ein Wert ohne Eintrag hier erscheint mit seinem Bezeichner.
/// </remarks>
internal sealed class EnumDisplayConverter : IValueConverter
{
    // Boxed: Gleiche Werte derselben Aufzählung sind gleich, gleiche Zahlen verschiedener nicht.
    private static readonly Dictionary<object, string> Texts = new()
    {
        [SearchMode.Fts5] = UiText.Get("Enum_SearchMode_Fts5"),
        [SearchMode.Regex] = UiText.Get("Enum_SearchMode_Regex"),
        [SimilarityMode.None] = UiText.Get("Enum_SimilarityMode_None"),
        [SimilarityMode.Stemmed] = UiText.Get("Enum_SimilarityMode_Stemmed"),
        [SimilarityMode.NearStem] = UiText.Get("Enum_SimilarityMode_NearStem"),
        [SimilarityMode.NearStemSynonyms] = UiText.Get("Enum_SimilarityMode_NearStemSynonyms"),
        [OperationHealth.Healthy] = UiText.Get("Enum_OperationHealth_Healthy"),
        [OperationHealth.Warning] = UiText.Get("Enum_OperationHealth_Warning"),
        [OperationHealth.Error] = UiText.Get("Enum_OperationHealth_Error"),
        [AppTheme.System] = UiText.Get("Enum_AppTheme_System"),
        [AppTheme.Light] = UiText.Get("Enum_AppTheme_Light"),
        [AppTheme.Dark] = UiText.Get("Enum_AppTheme_Dark"),
        [LogLevel.Trace] = UiText.Get("Enum_LogLevel_Trace"),
        [LogLevel.Debug] = UiText.Get("LogViewer_Level_Debug"),
        [LogLevel.Information] = UiText.Get("LogViewer_Level_Information"),
        [LogLevel.Warning] = UiText.Get("LogViewer_Level_Warning"),
        [LogLevel.Error] = UiText.Get("LogViewer_Level_Error"),
        [LogLevel.Critical] = UiText.Get("LogViewer_Level_Critical"),
    };

    /// <summary>Anzeigetext eines Werts.</summary>
    /// <param name="value">Ein Wert einer der Aufzählungen.</param>
    public static string Display(object? value) => value switch
    {
        null => string.Empty,
        _ when Texts.TryGetValue(value, out string? text) => text,
        _ => value.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Display(value);

    /// <inheritdoc />
    /// <remarks>Die Anzeige ist nur lesend; gewählt wird über das Element selbst.</remarks>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
