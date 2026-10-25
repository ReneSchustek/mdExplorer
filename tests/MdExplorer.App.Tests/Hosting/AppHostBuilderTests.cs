using System.Windows;
using MdExplorer.App.Hosting;
using MdExplorer.TagCloud.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Der Container der Anwendung in der Umgebung „Development“: Dort prüft er bei jeder Auflösung,
/// ob ein langlebiger Dienst einen kurzlebigen festhält. In der ausgelieferten Fassung ist die
/// Prüfung aus, ein solcher Fehler fiele dort erst als Absturz unter Last auf.
/// </summary>
public sealed class AppHostBuilderTests
{
    [Fact]
    public void Build_TagCloudViewModel_DoesNotCaptureScopedServices()
    {
        using IHost host = BuildValidatingHost();

        TagCloudViewModel viewModel = host.Services.GetRequiredService<TagCloudViewModel>();

        Assert.NotNull(viewModel);
    }

    /// <remarks>
    /// Jede Registrierung wird einzeln aufgelöst, auch die über eine Fabrik: Die Prüfung beim
    /// Bauen sieht in eine Fabrik nicht hinein, eine fehlende Abhängigkeit darin fiele erst beim
    /// ersten Öffnen des betroffenen Bereichs auf. Fenster und Steuerelemente bleiben außen vor;
    /// sie lassen sich nur mit laufender Oberfläche erzeugen.
    /// </remarks>
    [Fact]
    public void CreateBuilder_InDevelopment_ResolvesEveryRegistration()
    {
        HostApplicationBuilder builder = AppHostBuilder.CreateBuilder(DevelopmentSettings());
        Type[] serviceTypes =
        [
            .. builder.Services
                .Select(descriptor => descriptor.ServiceType)
                .Where(type => !type.IsGenericTypeDefinition && !typeof(DependencyObject).IsAssignableFrom(type))
                .Distinct(),
        ];
        using IHost host = builder.Build();
        using IServiceScope scope = host.Services.CreateScope();

        List<string> failures = [];
        foreach (Type serviceType in serviceTypes)
        {
            try
            {
                _ = scope.ServiceProvider.GetServices(serviceType).ToList();
            }
            catch (InvalidOperationException exception)
            {
                failures.Add($"{serviceType.FullName}: {exception.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Settings_ReadConfigurationNextToTheProgram()
        => Assert.Equal(AppContext.BaseDirectory, AppHostBuilder.Settings().ContentRootPath);

    private static IHost BuildValidatingHost() => AppHostBuilder.Build(DevelopmentSettings());

    private static HostApplicationBuilderSettings DevelopmentSettings()
    {
        HostApplicationBuilderSettings settings = AppHostBuilder.Settings();
        settings.EnvironmentName = Environments.Development;
        return settings;
    }
}
