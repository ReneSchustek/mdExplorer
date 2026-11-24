using MdExplorer.App.Localization;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.Logging;

/// <summary>Auswahleintrag des Zeitraumfilters.</summary>
/// <param name="Window">Wie weit zurück; <see langword="null"/> für alles.</param>
/// <param name="Display">Anzeige-Label.</param>
internal sealed record LogPeriodFilter(TimeSpan? Window, string Display);
