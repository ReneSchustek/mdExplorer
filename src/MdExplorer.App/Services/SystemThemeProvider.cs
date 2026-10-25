using Microsoft.Win32;

using System.Diagnostics.CodeAnalysis;

namespace MdExplorer.App.Services;

/// <summary>
/// Liest das Windows-Apps-Theme aus der Registry (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>).
/// Nur <c>AppsUseLightTheme=0</c> bedeutet dunkel. Fehlt der Schlüssel oder scheitert das Lesen an
/// Rechten oder E/A, gilt hell: Dunkle Schrift auf hellem Grund bleibt in jedem Fall lesbar.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class SystemThemeProvider : ISystemThemeProvider
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <inheritdoc />
    public bool IsDarkMode
    {
        get
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                if (key is null)
                {
                    return false;
                }
                object? value = key.GetValue(AppsUseLightThemeValue);
                return value is int integer && integer == 0;
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        }
    }
}
