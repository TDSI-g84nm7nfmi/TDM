using System.Collections.ObjectModel;
using System.Windows.Input;
using TDM.Models;
using TDM.Services;

namespace TDM.ViewModels
{
    public class HistoryViewModel : ObservableObject
    {
        public ObservableCollection<HistoryEntry> Entries { get; } = new();

        private HistoryEntry? _selectedEntry;
        public HistoryEntry? SelectedEntry
        {
            get => _selectedEntry;
            set { if (SetProperty(ref _selectedEntry, value)) ((RelayCommand)RemoveCommand).RaiseCanExecuteChanged(); }
        }

        public ICommand RemoveCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand CopyUrlCommand { get; }

        public HistoryViewModel()
        {
            RemoveCommand = new RelayCommand(_ => Remove(), _ => SelectedEntry != null);
            ClearCommand = new RelayCommand(_ => Clear());
            OpenFolderCommand = new RelayCommand(_ => OpenFolder(), _ => SelectedEntry != null);
            RetryCommand = new RelayCommand(_ => Retry(), _ => SelectedEntry != null);
            CopyUrlCommand = new RelayCommand(_ => CopyUrl(), _ => SelectedEntry != null);

            HistoryService.Changed += (_, _) => Refresh();
            Refresh();
        }

        public void Refresh()
        {
            Entries.Clear();
            foreach (var e in HistoryService.Entries)
                Entries.Add(e);
        }

        public void Remove()
        {
            if (SelectedEntry == null) return;
            HistoryService.Remove(SelectedEntry);
            HistoryService.Save();
        }

        public void Clear()
        {
            HistoryService.Clear();
            HistoryService.Save();
        }

        public void OpenFolder()
        {
            if (SelectedEntry == null) return;
            var dir = System.IO.Path.GetDirectoryName(SelectedEntry.FilePath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
        }

        public void Retry()
        {
            if (SelectedEntry == null || string.IsNullOrEmpty(SelectedEntry.Url)) return;
            var dir = System.IO.Path.GetDirectoryName(SelectedEntry.FilePath);
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
                dir = SettingsService.Current.DefaultSaveDir;
            DownloadManager.Instance.Add(SelectedEntry.Url, dir);
        }

        public void CopyUrl()
        {
            if (SelectedEntry == null) return;
            try { System.Windows.Clipboard.SetText(SelectedEntry.Url); } catch { }
        }
    }
}
