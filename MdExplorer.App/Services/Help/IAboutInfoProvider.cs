namespace MdExplorer.App.Services.Help;

/// <summary>Liefert die Inhalte für den „Über MdExplorer…"-Dialog.</summary>
internal interface IAboutInfoProvider
{
    /// <summary>Erzeugt eine aktuelle <see cref="AboutInfo"/>-Instanz.</summary>
    AboutInfo Read();
}
