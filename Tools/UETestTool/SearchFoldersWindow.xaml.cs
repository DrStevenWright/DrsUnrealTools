using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Drs.Unreal.Common;
using Microsoft.Win32;

namespace UETestTool;

/// <summary>Edits the user's project search folders (stored in the app's settings.json, not in any project).</summary>
public partial class SearchFoldersWindow : Window
{
    private readonly ObservableCollection<string> _folders;

    public SearchFoldersWindow(IEnumerable<string> folders)
    {
        InitializeComponent();
        _folders = new ObservableCollection<string>(folders);
        FolderList.ItemsSource = _folders;
    }

    /// <summary>The edited list, once OK is clicked.</summary>
    public IReadOnlyList<string> Folders => _folders;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add search folders", Multiselect = true };
        if (FolderList.SelectedItem is string current && Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) != true) return;

        foreach (var picked in dialog.FolderNames)
        {
            string path;
            try { path = PathUtil.Normalize(picked); } catch (Exception) { continue; }
            if (_folders.Any(f => PathUtil.Comparer.Equals(f, path))) continue;
            _folders.Add(path);
            FolderList.SelectedItem = path;
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in FolderList.SelectedItems.Cast<string>().ToList()) _folders.Remove(item);
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveButton.IsEnabled = FolderList.SelectedItems.Count > 0;

    private void FolderList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) Remove_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
