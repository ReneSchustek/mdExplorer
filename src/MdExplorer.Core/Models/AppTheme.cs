using System.ComponentModel.DataAnnotations;

namespace MdExplorer.Core.Models;

/// <summary>Theme-Wahl für die Darstellung.</summary>
public enum AppTheme
{
    /// <summary>Folgt dem Betriebssystem-Theme.</summary>
    System = 0,

    /// <summary>Helles Theme erzwungen.</summary>
    Light = 1,

    /// <summary>Dunkles Theme erzwungen.</summary>
    Dark = 2,
}
