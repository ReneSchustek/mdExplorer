using System.Windows;
using System.Windows.Threading;

namespace MdExplorer.App.Tests.Views;

/// <summary>
/// Die gemeinsame Testanwendung lebt so lange wie der Testprozess.
/// </summary>
/// <remarks>
/// WPF beendet eine Anwendung in der Vorgabe, sobald auf ihrem Thread das letzte Fenster schließt.
/// Danach ist <see cref="Application.Current"/> leer, und eine zweite Anwendung lässt sich im selben
/// Prozess nicht mehr anlegen: Jede weitere Prüfung mit Ressourcen scheiterte. Welche Prüfung die
/// Anwendung anlegt, hängt von der Reihenfolge der Testklassen ab; der Fehler trat deshalb nur in
/// manchen Läufen auf.
/// </remarks>
public sealed class ApplicationResourcesTests
{
    [Fact]
    public void TheTestApplication_EndsOnlyWhenAskedTo() => StaRunner.Run(() =>
    {
        Dispatcher? owner = null;
        ApplicationResources.Use(() => owner = Application.Current.Dispatcher);

        // Der Modus ist nur auf dem Thread der Anwendung lesbar. Lebt dieser Thread nicht mehr,
        // gehörte die Anwendung einer früheren Prüfung, und ein Fenster dort hätte sie beendet.
        Assert.True(owner!.Thread.IsAlive);
        Assert.NotEqual(Thread.CurrentThread, owner.Thread);
        Assert.Equal(ShutdownMode.OnExplicitShutdown, owner.Invoke(() => Application.Current.ShutdownMode));
    });
}
