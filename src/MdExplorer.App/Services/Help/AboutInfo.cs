namespace MdExplorer.App.Services.Help;

/// <summary>Über-Dialog-Inhalt: Versionsangaben und Lizenz-Liste.</summary>
/// <param name="Version">Anzeige-Version (z. B. <c>1.0.0+git-sha</c>).</param>
/// <param name="BuildDateUtc">Änderungszeit der laufenden Programmdatei (UTC), als Näherung für das Build-Datum.</param>
/// <param name="Libraries">Liste der eingesetzten Open-Source-Bibliotheken.</param>
internal sealed record AboutInfo(
    string Version,
    DateTime BuildDateUtc,
    IReadOnlyList<LibraryInfo> Libraries);

/// <summary>Ein eingesetztes Open-Source-Paket mit Lizenzhinweis.</summary>
/// <param name="Name">Paketname (NuGet-ID).</param>
/// <param name="License">Lizenzkurzbezeichnung (SPDX-konform, z. B. <c>MIT</c>).</param>
internal sealed record LibraryInfo(string Name, string License);
