using System.Security.Principal;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Namen von Sperre und Kanal, über die sich die Instanzen eines Benutzers finden.
/// </summary>
/// <remarks>
/// Je Benutzer, nicht je Sitzung: Datenbank und Einstellungen liegen im Profil des Benutzers.
/// Zwei Sitzungen desselben Benutzers teilten sie sich, deshalb gilt die Sperre über alle
/// Sitzungen hinweg. Die Kennung des Benutzers im Namen trennt verschiedene Benutzer am selben
/// Rechner.
/// </remarks>
/// <param name="LockName">Name der Sperre, ohne Namensraum-Vorsilbe.</param>
/// <param name="PipeName">Name des Kanals.</param>
internal sealed record InstanceIdentity(string LockName, string PipeName)
{
    private const string Prefix = "MdExplorer-";

    /// <summary>Die Namen für den angemeldeten Benutzer.</summary>
    public static InstanceIdentity ForCurrentUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string userKey = identity.User?.Value
            ?? throw new InvalidOperationException("Der angemeldete Benutzer hat keine Sicherheitskennung.");
        return ForUser(userKey);
    }

    /// <summary>Die Namen für einen Benutzer.</summary>
    /// <param name="userKey">Eindeutige Kennung des Benutzers.</param>
    public static InstanceIdentity ForUser(string userKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        return new InstanceIdentity(Prefix + userKey, Prefix + userKey);
    }
}
