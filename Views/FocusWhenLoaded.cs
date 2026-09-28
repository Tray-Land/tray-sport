using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TraySport.Views;

internal static class FocusWhenLoaded
{
    /// <summary>
    /// Focuses a control on a freshly shown page. A page that isn't in the tree yet ignores Focus,
    /// and once it loads WinUI moves focus to its first control, so wait for Loaded and then one
    /// more (low priority) dispatcher turn.
    /// </summary>
    public static void Focus(Control control)
    {
        if (control.IsLoaded)
        {
            Enqueue(control);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            control.Loaded -= OnLoaded;
            Enqueue(control);
        }

        control.Loaded += OnLoaded;
    }

    private static void Enqueue(Control control) =>
        control.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => control.Focus(FocusState.Programmatic));
}
