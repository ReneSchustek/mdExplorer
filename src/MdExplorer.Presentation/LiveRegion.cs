using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Data;

namespace MdExplorer.Presentation;

/// <summary>
/// Macht ein Element zur Live-Region, die ihre Änderungen einem Screenreader ansagt.
/// </summary>
/// <remarks>
/// <para>
/// In WPF sagt <see cref="AutomationProperties.LiveSettingProperty"/> allein nichts an. Ein
/// Screenreader liest erst, wenn das Element das Ereignis <see cref="AutomationEvents.LiveRegionChanged"/>
/// meldet. Dieses Verhalten meldet es bei jeder Aktualisierung einer Bindung am Element; die Bindung
/// braucht dafür <c>NotifyOnTargetUpdated=True</c>. Wird der Inhalt im Code gesetzt, ruft der Code
/// <see cref="AnnounceNow"/>.
/// </para>
/// <para>
/// Über das Routed Event der Bindung statt über eine Beobachtung der Eigenschaft: Eine solche hält
/// das Element in einer statischen Tabelle fest, bis sie abgemeldet wird.
/// </para>
/// </remarks>
public static class LiveRegion
{
    /// <summary>Ob das Element seine Änderungen ansagt.</summary>
    public static readonly DependencyProperty AnnounceProperty = DependencyProperty.RegisterAttached(
        "Announce",
        typeof(bool),
        typeof(LiveRegion),
        new PropertyMetadata(false, OnAnnounceChanged));

    /// <summary>Liest <see cref="AnnounceProperty"/>.</summary>
    /// <param name="element">Das Element.</param>
    public static bool GetAnnounce(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(AnnounceProperty);
    }

    /// <summary>Setzt <see cref="AnnounceProperty"/>.</summary>
    /// <param name="element">Das Element.</param>
    /// <param name="value">Ob es seine Änderungen ansagt.</param>
    public static void SetAnnounce(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AnnounceProperty, value);
    }

    /// <summary>Sagt den jetzigen Inhalt des Elements an.</summary>
    /// <param name="element">Eine Live-Region.</param>
    public static void AnnounceNow(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        UIElementAutomationPeer.CreatePeerForElement(element).RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static void OnAnnounceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not UIElement live)
        {
            return;
        }

        live.RemoveHandler(Binding.TargetUpdatedEvent, (EventHandler<DataTransferEventArgs>)OnTargetUpdated);
        if ((bool)args.NewValue)
        {
            AutomationProperties.SetLiveSetting(live, AutomationLiveSetting.Polite);
            live.AddHandler(Binding.TargetUpdatedEvent, (EventHandler<DataTransferEventArgs>)OnTargetUpdated);
        }
    }

    private static void OnTargetUpdated(object? sender, DataTransferEventArgs args)
    {
        if (sender is UIElement live)
        {
            AnnounceNow(live);
        }
    }
}
