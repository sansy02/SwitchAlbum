using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwitchAlbum.Models;
using SwitchAlbum.Resources;

namespace SwitchAlbum.ViewModels;

/// <summary>单个游戏（或全部）的照片网格。行分块 + ListBox 虚拟化以支持大相册。</summary>
public sealed partial class GameGridViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public GameGridViewModel(string title, IReadOnlyList<AlbumItem> items, MainViewModel main)
    {
        Title = title;
        _main = main;
        Items = items.Select(i => new PhotoItemViewModel(i, main)).ToList();
        foreach (var item in Items)
        {
            item.SelectionChanged += OnItemSelectionChanged;
        }

        SetColumns(4);
    }

    public string Title { get; }
    public IReadOnlyList<PhotoItemViewModel> Items { get; }
    public ObservableCollection<PhotoRow> Rows { get; } = new();

    [ObservableProperty] private int _columns;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _selectedText = "";
    [ObservableProperty] private bool _canSaveSelected;

    public void SetColumns(int columns)
    {
        if (columns < 2)
        {
            columns = 2;
        }

        if (columns == Columns)
        {
            return;
        }

        Columns = columns;
        Rows.Clear();
        foreach (var chunk in Items.Chunk(columns))
        {
            Rows.Add(new PhotoRow(chunk));
        }
    }

    private void OnItemSelectionChanged(object? sender, bool selected)
    {
        SelectedCount = Math.Max(0, SelectedCount + (selected ? 1 : -1));
        SelectedText = string.Format(Strings.Lbl_Selected, SelectedCount);
        CanSaveSelected = SelectedCount > 0;
    }

    public IReadOnlyList<AlbumItem> SelectedItems()
        => Items.Where(i => i.IsSelected).Select(i => i.Item).ToList();

    public void SetAllSelected(bool selected)
    {
        foreach (var item in Items)
        {
            item.IsSelected = selected;
        }
    }

    public void RefreshSaved()
    {
        foreach (var item in Items)
        {
            item.RefreshSaved();
        }
    }

    public void OpenLightbox(PhotoItemViewModel item) => _main.OpenLightbox(Items, item);

    public void OpenVideo(PhotoItemViewModel item) => _ = _main.OpenVideoAsync(item);
}

public sealed class PhotoRow
{
    public PhotoRow(IReadOnlyList<PhotoItemViewModel> items) => Items = items;

    public IReadOnlyList<PhotoItemViewModel> Items { get; }
}
