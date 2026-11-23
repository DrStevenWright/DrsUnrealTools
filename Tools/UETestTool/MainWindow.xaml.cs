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
        _settings = AppSettings.Load();
        _vm = new MainViewModel(_settings, this);
        DataContext = _vm;

        RestorePlacement();
        Loaded += async (_, _) => await _vm.InitializeAsync();
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

    public IReadOnlyList<string>? EditSearchFolders(IReadOnlyList<string> folders, IReadOnlyList<string> unrealFolders)
    {
        var dialog = new SearchFoldersWindow(folders, unrealFolders) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Folders.ToList() : null;
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
