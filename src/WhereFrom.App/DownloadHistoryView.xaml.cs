using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using WhereFrom.Platform.Windows;
using WhereFrom.Storage;

namespace WhereFrom.App;

public partial class DownloadHistoryView : UserControl
{
    private readonly ObservableCollection<HistoryRow> rows = [];
    private DownloadStore? store;
    private UnifiedProvenanceQuery? query;
    private CancellationTokenSource? searchRequest;
    private DownloadHistoryCursor? next;
    private DownloadHistoryFilter currentFilter = new();
    private object? pathCheck;

    public DownloadHistoryView()
    {
        InitializeComponent();
        HistoryList.ItemsSource = rows;
    }

    internal event EventHandler? FileTabRequested;

    internal void Configure(DownloadStore downloads, UnifiedProvenanceQuery provenance)
    {
        store = downloads;
        query = provenance;
    }

    internal void Open()
    {
        if (DetailPanel.Visibility != Visibility.Visible)
        {
            _ = LoadAsync(reset: true);
            SearchBox.Focus();
        }
    }

    internal void CancelSearch()
    {
        var request = searchRequest;
        searchRequest = null;
        request?.Cancel();
    }

    private async Task LoadAsync(bool reset, bool debounce = false)
    {
        if (store is null)
            return;

        CancelSearch();
        using var request = new CancellationTokenSource();
        searchRequest = request;
        var token = request.Token;
        if (reset)
        {
            var browser = (BrowserFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            DateTimeOffset? since = int.TryParse((TimeFilter.SelectedItem as ComboBoxItem)?.Tag as string, out var days)
                ? new DateTimeOffset(DateTime.Today.AddDays(1 - days)) : null;
            currentFilter = new(SearchBox.Text.Trim(), browser, since);
            rows.Clear();
            next = null;
        }
        var filter = currentFilter;
        LoadMoreButton.IsEnabled = false;
        FileTabButton.Visibility = Visibility.Collapsed;
        ListStatus.Text = "正在读取下载记录…";

        try
        {
            if (debounce)
                await Task.Delay(180, token);
            var cursor = next;
            var page = await Task.Run(() => store.ReadHistory(filter, cursor, token), token);
            token.ThrowIfCancellationRequested();
            foreach (var entry in page.Entries)
                rows.Add(new(entry));
            next = page.Next;
            var isFiltered = filter.Search.Length > 0 || filter.Browser is not null || filter.Since is not null;
            ListStatus.Text = rows.Count > 0 ? $"已显示 {rows.Count} 条记录"
                : isFiltered ? "没有符合搜索条件的记录"
                : "暂无下载记录。这里显示 WhereFrom 已捕获的下载。";
            FileTabButton.Visibility = rows.Count == 0 && !isFiltered
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is SqliteException or IOException or UnauthorizedAccessException
            or InvalidDataException or FormatException)
        {
            if (!token.IsCancellationRequested)
                ListStatus.Text = "下载记录读取失败。可点击刷新重试。";
        }
        finally
        {
            if (searchRequest == request)
            {
                searchRequest = null;
                LoadMoreButton.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
                LoadMoreButton.IsEnabled = true;
            }
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        await LoadAsync(reset: true, debounce: true);

    private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        await LoadAsync(reset: true);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || rows.Count == 0)
            return;
        e.Handled = true;
        HistoryList.SelectedIndex = 0;
        FocusSelectedRow();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync(reset: true);

    private async void LoadMore_Click(object sender, RoutedEventArgs e) => await LoadAsync(reset: false);

    private void FileTab_Click(object sender, RoutedEventArgs e) => FileTabRequested?.Invoke(this, EventArgs.Empty);

    private async void HistoryRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { Content: HistoryRow row })
            await ShowDetailAsync(row);
    }

    private async void HistoryList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && HistoryList.SelectedItem is HistoryRow row)
        {
            e.Handled = true;
            await ShowDetailAsync(row);
        }
    }

    private async Task ShowDetailAsync(HistoryRow row)
    {
        if (query is null)
            return;
        HistoryList.SelectedItem = row;
        var download = row.Entry.Download;
        DetailName.Text = row.FileName;
        DetailSummary.Text = $"{row.Domain}  ·  {row.Browser}  ·  {row.Completed}";
        DetailPathStatus.Text = "正在检查原路径…";
        DetailFields.IsExpanded = false;
        DetailValues.Text = $"原路径  {download.Path}\n初始网址  {download.InitialUrl ?? "未记录"}"
            + $"\n最终网址  {download.FinalUrl ?? "未记录"}\n引用页面  {download.ReferrerUrl ?? "未记录"}";
        ListPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        BackButton.Focus();

        var request = new object();
        pathCheck = request;
        var status = await Task.Run(() => query.CheckOriginalDownloadPath(download));
        if (pathCheck != request)
            return;
        DetailPathStatus.Text = status switch
        {
            OriginalDownloadPathStatus.Unavailable => "原路径当前不可用。这里显示当时的下载记录。",
            OriginalDownloadPathStatus.DifferentFile => "原路径上的文件已变化。此历史记录不属于当前文件。",
            OriginalDownloadPathStatus.DirectMatch => "原路径、大小、修改时间和内容均匹配。相同内容仍可能来自副本。",
            OriginalDownloadPathStatus.Possible => "原路径存在。大小和修改时间相符，可能关联，内容未经验证。",
            _ => "无法验证原路径上的文件。这里显示当时的下载记录。"
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        pathCheck = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Visibility.Visible;
        FocusSelectedRow();
    }

    private void FocusSelectedRow()
    {
        if (HistoryList.SelectedItem is HistoryRow row)
        {
            HistoryList.ScrollIntoView(row);
            HistoryList.UpdateLayout();
            if (HistoryList.ItemContainerGenerator.ContainerFromItem(row) is ListViewItem item)
            {
                item.Focus();
                return;
            }
        }
        Keyboard.Focus(HistoryList);
    }

    private sealed record HistoryRow(DownloadHistoryEntry Entry)
    {
        public string FileName => Entry.FileName;
        public string Domain => Entry.SourceDomain ?? "未记录";
        public string Browser => Entry.Download.Browser switch
        {
            "chrome" => "Chrome",
            "edge" => "Edge",
            var browser => browser
        };
        public string Completed => Entry.Download.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string AccessibleName => $"{FileName}，{Domain}，{Browser}，{Completed}";
    }
}
