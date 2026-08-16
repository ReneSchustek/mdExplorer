using System.Runtime.ExceptionServices;
using Xunit.Sdk;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Führt eine Zusicherung auf einem STA-Thread aus.
/// </summary>
/// <remarks>
/// <para>
/// Alles aus WPF verlangt einen Thread im Einzelfaden-Modus — der Testläufer stellt keinen.
/// Ohne diesen Umweg lässt sich kein einziges Bedienelement erzeugen, und die Bausteine der
/// Gestaltungslinie blieben ungeprüft.
/// </para>
/// <para>
/// Herausgezogen am 16.08.2026 aus <c>HighlightToInlinesConverterTests</c>, als der zweite
/// Aufrufer dazukam.
/// </para>
/// </remarks>
internal static class StaRunner
{
    /// <summary>Führt <paramref name="assertion"/> auf einem eigenen STA-Thread aus.</summary>
    /// <param name="assertion">Die Zusicherung.</param>
    public static void Run(Action assertion)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        ExceptionDispatchInfo? failure = null;

        Thread thread = new(() =>
        {
            try
            {
                assertion();
            }
            catch (Exception exception) when (exception is XunitException or InvalidOperationException or System.Windows.Markup.XamlParseException)
            {
                // Eine fehlgeschlagene Zusicherung, ein Thread-Verstoß und ein nicht
                // auflösbarer Verweis im Markup sind das, was hier auftreten kann. Alle drei
                // gehören unverfälscht in den Testbericht, deshalb mit Ursprungs-Stapel
                // weitergereicht statt neu geworfen.
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
