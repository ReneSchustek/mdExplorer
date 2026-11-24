using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MdExplorer.App.ViewModels;

/// <summary>
/// Eine beobachtbare Sammlung, deren Inhalt sich in einem Schritt austauschen lässt.
/// </summary>
/// <remarks>
/// <see cref="ObservableCollection{T}"/> meldet jedes <c>Add</c> einzeln. Eine gruppierte Liste
/// ordnet bei jeder Meldung neu ein; tausend Einträge hießen tausend Durchläufe, bei jedem
/// Tastendruck im Filter. <see cref="ReplaceAll"/> meldet genau einmal, als Neuanfang.
/// </remarks>
/// <typeparam name="T">Typ der Einträge.</typeparam>
internal sealed class ReplaceableCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));

    // So nennt ObservableCollection selbst die Indexer-Meldung; Bindungen an Einträge hängen daran.
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

    /// <summary>Ersetzt den gesamten Inhalt und meldet das einmal.</summary>
    /// <param name="items">Der neue Inhalt in seiner Reihenfolge.</param>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        Items.Clear();
        foreach (T item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
