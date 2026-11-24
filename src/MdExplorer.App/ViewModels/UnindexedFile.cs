using System.IO;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Hosting;
using MdExplorer.Core.Text;
using MdExplorer.Parser.Abstractions;
using MdExplorer.Parser.Models;
using Microsoft.Extensions.Logging;

namespace MdExplorer.App.ViewModels;

/// <summary>Eine direkt gelesene Datei: die Bytes für den Editor, die Übersetzung für die Vorschau.</summary>
/// <param name="Bytes">Der gelesene Inhalt.</param>
/// <param name="Parsed">Die Übersetzung durch den Parser.</param>
internal sealed record UnindexedFile(byte[] Bytes, ParseResult Parsed);
