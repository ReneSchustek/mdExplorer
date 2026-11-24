using MdExplorer.App.Localization;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Logging;

/// <summary>Auswahleintrag des Stufenfilters.</summary>
/// <param name="MinimumLevel">Untere Grenze: Einträge mit niedrigerem Level werden ausgeblendet.</param>
/// <param name="Display">Anzeige-Label.</param>
internal sealed record LogLevelFilter(LogLevel MinimumLevel, string Display);
