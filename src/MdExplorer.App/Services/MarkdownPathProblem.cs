using System.IO;
using MdExplorer.Core.Abstractions;

namespace MdExplorer.App.Services;

/// <summary>Warum ein Pfad von außen nicht geöffnet wird.</summary>
internal enum MarkdownPathProblem
{
    /// <summary>
    /// Leer, mit unzulässigen Zeichen, relativ, wo ein vollständiger Pfad verlangt ist, oder keine
    /// gewöhnliche Datei (Gerätepfad, alternativer Datenstrom).
    /// </summary>
    InvalidPath,

    /// <summary>Weder <c>.md</c> noch <c>.markdown</c>.</summary>
    UnsupportedExtension,

    /// <summary>Die Datei gibt es nicht.</summary>
    NotFound,
}
