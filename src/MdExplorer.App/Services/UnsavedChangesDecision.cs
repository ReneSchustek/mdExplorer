namespace MdExplorer.App.Services;

/// <summary>Antwort des Nutzers auf die Frage nach ungespeicherten Änderungen.</summary>
internal enum UnsavedChangesDecision
{
    /// <summary>Erst speichern, dann weiter.</summary>
    Save,

    /// <summary>Die Änderungen verwerfen und weiter.</summary>
    Discard,

    /// <summary>Beim Dokument bleiben; der Wechsel findet nicht statt.</summary>
    Cancel,
}
