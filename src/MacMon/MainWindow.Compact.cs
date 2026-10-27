using Avalonia;
using Avalonia.Controls;

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

    private void ApplyViewLayout()
    {
        _switchingView = true;
        try
        {
            WindowState = WindowState.Normal;
            MaxHeight = double.PositiveInfinity;
            MinWidth = CompactMode ? WindowViewState.CompactMinimumWidth : 800;
            MinHeight = CompactMode ? WindowViewState.CompactHeight : 760;
            MaxHeight = CompactMode ? WindowViewState.CompactHeight : double.PositiveInfinity;
            Width = CompactMode ? _viewState.CompactWidth : _viewState.FullSize.Width;
            Height = CompactMode ? WindowViewState.CompactHeight : _viewState.FullSize.Height;
            MainLayout.IsVisible = !CompactMode;
            CompactPanel.IsVisible = CompactMode;
            CompactPanel.AlwaysOnTop = Topmost;
            JobsView.SetActive(!CompactMode && DataTabs.SelectedIndex == 1);
            if (IsVisible) FitWindowToScreen();
        }
        finally { _switchingView = false; }
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
