namespace MdExplorer.App.Services.Help;

/// <summary>Ein eingesetztes Open-Source-Paket mit Lizenzhinweis.</summary>
/// <param name="Name">Paketname (NuGet-ID).</param>
/// <param name="License">Lizenzkurzbezeichnung (SPDX-konform, z. B. <c>MIT</c>).</param>
internal sealed record LibraryInfo(string Name, string License);
