using System.Runtime.ExceptionServices;
using Xunit.Sdk;

namespace MdExplorer.TagCloud.Tests.Views;

/// <summary>
/// Führt eine Zusicherung auf einem STA-Thread aus. Bedienelemente aus WPF lassen sich nur dort
/// erzeugen, und der Testläufer stellt keinen solchen Thread.
/// </summary>
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
                // Mit Ursprungs-Stapel weitergereicht, damit der Testbericht die echte Stelle zeigt.
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
    }
}
