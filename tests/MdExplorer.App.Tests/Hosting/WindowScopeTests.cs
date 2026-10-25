using System.Windows;
using MdExplorer.App.Hosting;
using MdExplorer.App.Tests.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Ein Nebenfenster bekommt einen eigenen Scope. Was der Container dafür erzeugt hat, wird mit
/// dem Schließen freigegeben und nicht bis zum Ende der Anwendung festgehalten.
/// </summary>
public sealed class WindowScopeTests
{
    [Fact]
    public void Create_WhenTheWindowCloses_DisposesWhatTheScopeCreated()
        => StaRunner.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            Tracked? tracked = null;

            Window window = WindowScope.Create(provider, scoped =>
            {
                tracked = scoped.GetRequiredService<Tracked>();
                return OffScreenWindow();
            });
            window.Show();
            window.Close();

            Assert.True(tracked?.IsDisposed);
        });

    [Fact]
    public void Create_WhileTheWindowIsOpen_KeepsTheInstanceAlive()
        => StaRunner.Run(() =>
        {
            using ServiceProvider provider = BuildProvider();
            Tracked? tracked = null;

            Window window = WindowScope.Create(provider, scoped =>
            {
                tracked = scoped.GetRequiredService<Tracked>();
                return OffScreenWindow();
            });

            Assert.False(tracked?.IsDisposed);
            window.Close();
        });

    [Fact]
    public void Create_WhenBuildingFails_DisposesTheScope()
    {
        using ServiceProvider provider = BuildProvider();
        Tracked? tracked = null;

        _ = Assert.Throws<InvalidOperationException>(() => WindowScope.Create<Window>(provider, scoped =>
        {
            tracked = scoped.GetRequiredService<Tracked>();
            throw new InvalidOperationException("Fenster lässt sich nicht bauen.");
        }));

        Assert.True(tracked?.IsDisposed);
    }

    // Ein Fenster meldet sein Schließen erst, wenn es einmal angezeigt war. Außerhalb des
    // sichtbaren Bereichs blitzt es dabei nicht auf.
    private static Window OffScreenWindow() => new()
    {
        Left = -10_000,
        Top = -10_000,
        Width = 1,
        Height = 1,
        WindowStyle = WindowStyle.None,
        ShowInTaskbar = false,
        ShowActivated = false,
    };

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        _ = services.AddTransient(_ => new Tracked());
        return services.BuildServiceProvider();
    }

    /// <summary>Meldet, ob der Container ihn freigegeben hat.</summary>
    private sealed class Tracked : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
