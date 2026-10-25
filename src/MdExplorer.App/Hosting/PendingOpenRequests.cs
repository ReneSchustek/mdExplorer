using System.Threading;
using MdExplorer.App.Services;

namespace MdExplorer.App.Hosting;

/// <summary>
/// Hält Aufträge von außen zurück, bis das Hauptfenster steht, und reicht sie danach sofort
/// durch.
/// </summary>
/// <remarks>
/// Der Kanal lauscht schon vor dem Start der Dienste, damit ein zweiter Doppelklick während des
/// Startbilds nicht ins Leere geht. Bis zum Hauptfenster gilt nur der jüngste Auftrag mit Datei:
/// Der Dokumentbereich zeigt ohnehin nur eine. Ein Auftrag ohne Datei verdrängt keinen mit Datei.
/// </remarks>
internal sealed class PendingOpenRequests
{
    private readonly Lock _gate = new();
    private Action<MarkdownPathCheck>? _handler;
    private MarkdownPathCheck? _pending;

    /// <summary>Nimmt einen Auftrag an; aufrufbar von jedem Thread.</summary>
    /// <param name="request">Der geprüfte Auftrag.</param>
    public void Post(MarkdownPathCheck request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Action<MarkdownPathCheck>? handler;
        lock (_gate)
        {
            handler = _handler;
            if (handler is null)
            {
                if (_pending is null || !request.IsNone)
                {
                    _pending = request;
                }
                return;
            }
        }
        handler(request);
    }

    /// <summary>Verbindet den Empfänger und reicht den zurückgehaltenen Auftrag nach.</summary>
    /// <param name="handler">Der Empfänger; er wird auf dem Thread des jeweiligen Absenders aufgerufen.</param>
    public void Attach(Action<MarkdownPathCheck> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        MarkdownPathCheck? pending;
        lock (_gate)
        {
            if (_handler is not null)
            {
                throw new InvalidOperationException("Der Empfänger ist schon verbunden.");
            }
            _handler = handler;
            pending = _pending;
            _pending = null;
        }
        if (pending is not null)
        {
            handler(pending);
        }
    }
}
