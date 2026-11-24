using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using UETestTool.Services;
using UETestTool.ViewModels;

namespace UETestTool;

public partial class MainWindow : Window, IUserDialogs
{
    private readonly AppSettings _settings;
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        if (AppInfo.Version.Length > 0) Title = $"{Title} {AppInfo.Version}";
        _settings = AppSettings.Load();
        _vm = new MainViewModel(_settings, this);
        DataContext = _vm;

        RestorePlacement();
        Loaded += async (_, _) => await _vm.InitializeAsync();
        _vm.LaunchItems.CollectionChanged += (_, _) => ScheduleGrowToFit();
    }

    // ---- Growing to fit the configurations ----------------------------------------------------------

    private bool _growPending;
    private ScrollViewer? _listScroller;

    /// <summary>Grows the window once the list's new rows have been laid out.</summary>
    private void ScheduleGrowToFit()
    {
        if (_growPending) return;
        _growPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
        {
            _growPending = false;
            GrowToFitList();
        });
    }

    /// <summary>
    /// If the configurations and sessions don't all fit, makes the window taller by the missing amount, up to the
    /// bottom of the screen it's on. Never shrinks it, and leaves a maximised window alone.
    /// </summary>
    private void GrowToFitList()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        _listScroller ??= FindDescendant<ScrollViewer>(ConfigList);
        if (_listScroller is null) return;

        var missing = _listScroller.ExtentHeight - _listScroller.ViewportHeight;
        if (missing < 1) return;
        var height = Math.Min(ActualHeight + Math.Ceiling(missing), WorkAreaBottom() - Top);
        if (height > ActualHeight) Height = height;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>The bottom of the usable area (above the taskbar) of the monitor the window is on, in WPF units.</summary>
    private double WorkAreaBottom()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (hwnd == IntPtr.Zero
            || !NativeMethods.GetMonitorInfo(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest), ref info)
            || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            return SystemParameters.WorkArea.Bottom;
        return target.TransformFromDevice.Transform(new Point(0, info.Work.Bottom)).Y;
    }

    private static class NativeMethods
    {
        public const uint MonitorDefaultToNearest = 2;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _vm.FlushUserState();
        SavePlacement();
        _settings.Save();
        base.OnClosing(e);
    }

    // ---- IUserDialogs -------------------------------------------------------------------------------

    public bool Confirm(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public string? PickProjectFile(string? initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add an Unreal project",
            Filter = "Unreal project (*.uproject)|*.uproject",
            CheckFileExists = true,
        };
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = Path.GetDirectoryName(initialDirectory) ?? initialDirectory;
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string>? EditSearchFolders(IReadOnlyList<string> folders)
    {
        var dialog = new SearchFoldersWindow(folders) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Folders.ToList() : null;
    }

    public ConfigEdit? EditConfig(Drs.Unreal.Common.Launch.LaunchConfig? config,
        IReadOnlyList<Drs.Unreal.Common.Launch.LaunchConfig> configs, Drs.Unreal.Common.Launch.LaunchConfig? suggestedTemplate)
    {
        var dialog = new ConfigWindow(config, configs, suggestedTemplate) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    public SessionEdit? EditSession(Drs.Unreal.Common.Launch.LaunchSession? session,
        IReadOnlyList<Drs.Unreal.Common.Launch.LaunchConfig> configs)
    {
        var dialog = new SessionWindow(session, configs) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    // ---- Event handlers -----------------------------------------------------------------------------

    private void RemoveProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProjectItem project)
            _vm.RemoveProjectCommand.Execute(project);
    }

    /// <summary>
    /// Focusing a row's text box selects that row. (Done here rather than with an IsKeyboardFocusWithin style
    /// trigger, which would deselect the row again as soon as focus left it.)
    /// </summary>
    private void Configs_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        for (var d = e.NewFocus as DependencyObject; d is not null && d != sender; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ListBoxItem item)
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    private void CopyCommandLine_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ILaunchItem item)
            _vm.CopyCommandLineCommand.Execute(item);
    }

    /// <summary>The New button opens its menu below itself on a left click.</summary>
    private void NewMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Keeps the selected row visible, e.g. a session selected on load, below the configurations.</summary>
    private void Configs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is { } item) list.ScrollIntoView(item);
    }

    /// <summary>Double-clicking a configuration row (but not inside its text box or buttons) launches it.</summary>
    private void Configs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != sender; d = VisualTreeHelper.GetParent(d))
        {
            if (d is TextBox or System.Windows.Controls.Primitives.ButtonBase) return;
            if (d is ListBoxItem)
            {
                _vm.LaunchCommand.Execute(null);
                return;
            }
        }
    }

    // ---- Window placement ---------------------------------------------------------------------------

    private void RestorePlacement()
    {
        if (_settings.Window is not { } w || w.Width < MinWidth || w.Height < MinHeight) return;
        // Only restore if the window would be visible on the current desktop (monitors may have changed).
        var visible = w.Left + 100 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                      && w.Top + 50 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight
                      && w.Left + w.Width - 100 > SystemParameters.VirtualScreenLeft
                      && w.Top > SystemParameters.VirtualScreenTop - 10;
        if (!visible) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = w.Left;
        Top = w.Top;
        Width = w.Width;
        Height = w.Height;
        if (w.Maximized) WindowState = WindowState.Maximized;
    }

    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Window = new AppSettings.WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
    }
}
