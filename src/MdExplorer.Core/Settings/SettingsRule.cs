using System.Collections.Immutable;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using Microsoft.Extensions.FileSystemGlobbing;

namespace MdExplorer.Core.Settings;

/// <summary>Die Regeln, gegen die ein Einstellungswert verstoßen kann.</summary>
public enum SettingsRule
{
    /// <summary>Der Eintrag ist leer.</summary>
    ValueEmpty,

    /// <summary>Der Pfad ist nicht absolut.</summary>
    PathNotAbsolute,

    /// <summary>Das Verzeichnis existiert nicht.</summary>
    DirectoryMissing,

    /// <summary>Ein Ausrufezeichen ohne folgendes Muster.</summary>
    NegationWithoutPattern,

    /// <summary>Das Muster ist syntaktisch falsch.</summary>
    PatternInvalid,

    /// <summary>Das Muster ist ein absoluter Pfad und träfe nie.</summary>
    PatternRooted,

    /// <summary>Das Muster nutzt Klammern, die die Mustersprache nicht kennt.</summary>
    PatternUnsupportedSyntax,

    /// <summary>Der Wert ist der Anwendung unbekannt.</summary>
    UnknownValue,

    /// <summary>Der Zahlenwert liegt außerhalb seiner Grenzen.</summary>
    OutOfRange,
}
