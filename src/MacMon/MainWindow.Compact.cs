using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MacMon;

public sealed partial class MainWindow
{
    private void SetCompactView(bool compact)
    {
        if (_closing || CompactMode == compact) return;
        _captureCard.Hide();
        Chart.ClearHover();
        HideResourceDetails();
        _viewState.ObserveSize(ClientSize, WindowState == WindowState.Normal);
        _viewState.SetMode(compact);
        ApplyViewLayout();
        Update();
        if (compact) CompactPanel.FocusFullView();
        else EnterCompactView.Focus();
    }

    private int _viewLayoutRevision;

    private void ApplyViewLayout()
    {
        _switchingView = true;
        var revision = ++_viewLayoutRevision;
        try
        {
            WindowState = WindowState.Normal;
            // Release the old native constraints before changing the content and size.
            // In particular, Full view must never resize with Compact's fixed maximum height.
            MaxHeight = double.PositiveInfinity;
            MinHeight = 0;
            MinWidth = 0;
            MainLayout.IsVisible = !CompactMode;
            CompactPanel.IsVisible = CompactMode;
            Width = CompactMode ? _viewState.CompactWidth : _viewState.FullSize.Width;
            Height = CompactMode ? WindowViewState.CompactHeight : _viewState.FullSize.Height;
            MinWidth = CompactMode ? WindowViewState.CompactMinimumWidth : 800;
            MinHeight = CompactMode ? WindowViewState.CompactHeight : 760;
            MaxHeight = CompactMode ? WindowViewState.CompactHeight : double.PositiveInfinity;
            CompactPanel.AlwaysOnTop = Topmost;
            JobsView.SetActive(!CompactMode && DataTabs.SelectedIndex == 1);
            if (IsVisible) FitWindowToScreen();
        }
        finally
        {
            // AppKit delivers native resizes after the setters return. Keep those
            // intermediate bounds out of the saved view size and relayout afterward.
            Dispatcher.UIThread.Post(() =>
            {
                if (revision != _viewLayoutRevision) return;
                InvalidateMeasure();
                UpdateLayout();
                InvalidateVisual();
                _switchingView = false;
            }, DispatcherPriority.Loaded);
        }
    }

    private void FitWindowToScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;
        var work = screen.WorkingArea;
        Width = Math.Min(Width, Math.Max(MinWidth, work.Width / screen.Scaling - 40));
        Height = Math.Min(Height, Math.Max(MinHeight, work.Height / screen.Scaling - 60));
        var right = Math.Max(work.X, work.Right - (int)Math.Ceiling(Width * screen.Scaling));
        var bottom = Math.Max(work.Y, work.Bottom - (int)Math.Ceiling((Height + 30) * screen.Scaling));
        Position = new PixelPoint(Math.Clamp(Position.X, work.X, right), Math.Clamp(Position.Y, work.Y, bottom));
    }
}
