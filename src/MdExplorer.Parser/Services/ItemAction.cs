using System.Text;

namespace MdExplorer.Parser.Services;

/// <summary>Was mit einem Eintrag geschieht.</summary>
internal enum ItemAction
{
    /// <summary>Der Eintrag bleibt, wie er ist.</summary>
    Keep,

    /// <summary>Der Eintrag bekommt einen neuen Namen.</summary>
    Rename,

    /// <summary>Der Eintrag fällt weg: gelöscht, oder durch Zusammenführen doppelt.</summary>
    Drop,
}
