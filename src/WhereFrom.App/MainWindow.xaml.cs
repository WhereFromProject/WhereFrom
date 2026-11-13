using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WhereFrom.Core;
using WhereFrom.Platform.Windows;
using WhereFrom.Storage;

namespace WhereFrom.App;

public partial class MainWindow : Window
{
    private readonly UnifiedProvenanceQuery query;
    private bool isProcessing;

    public MainWindow() : this(new DownloadStore(DownloadStore.DefaultPath))
    {
    }

    internal MainWindow(DownloadStore store)
    {
        query = new(new WindowsZoneProvider(), store);
        InitializeComponent();
        HistoryView.Configure(store, query);
        HistoryView.FileTabRequested += (_, _) =>
        {
            FileTab.IsSelected = true;
            SelectFileButton.Focus();
        };
        Closed += (_, _) => HistoryView.CancelSearch();
    }

    public void OpenStartupFile(string path) => _ = InspectFileAsync(path);

    private void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Navigation || HistoryView is null)
            return;
        if (HistoryTab.IsSelected)
            HistoryView.Open();
        else
            HistoryView.CancelSearch();
    }

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new OpenFileDialog { Filter = "所有文件|*.*", Multiselect = false };
            if (picker.ShowDialog(this) == true)
                await InspectFileAsync(picker.FileName);
        }
        catch (ExternalException)
        {
            ActionStatus.Text = "无法打开文件选择器。";
        }
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = !isProcessing && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (isProcessing)
            return;
        FileTab.IsSelected = true;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths)
        {
            ActionStatus.Text = "请拖入一个本地文件。";
            return;
        }
        await InspectFileAsync(paths[0]);
    }

    private async Task InspectFileAsync(string path)
    {
        if (isProcessing)
            return;
        FileTab.IsSelected = true;
        isProcessing = true;
        SelectFileButton.IsEnabled = false;
        DropPanel.Visibility = Visibility.Collapsed;
        ResultScroll.Visibility = Visibility.Visible;
        ResultScroll.ScrollToTop();
        EvidencePanel.Visibility = Visibility.Collapsed;
        FileNameText.Text = Path.GetFileName(path) is { Length: > 0 } name ? name : "文件查询";
        SourceSummary.Text = "";
        TimeSummary.Text = "";
        ResultStatus.Text = "正在读取来源…";
        ActionStatus.Text = "";

        try
        {
            var (report, raw) = await Task.Run(() => query.InspectWithWindowsMetadata(path));
            DisplayResult(report, raw);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            ResultStatus.Text = "读取失败，无法查看此文件的来源。";
        }
        finally
        {
            SelectFileButton.IsEnabled = true;
            isProcessing = false;
        }
    }

    private void DisplayResult(ProvenanceReport report, string? raw)
    {
        var windows = report.Windows;
        var windowsDomain = ProvenanceUrlSanitizer.GetHttpDomain(windows.SourceUrl);
        var browserDomains = report.BrowserDownloads.Select(download =>
            ProvenanceUrlSanitizer.GetHttpDomain(download.FinalUrl ?? download.InitialUrl));
        var domains = browserDomains.Prepend(windowsDomain).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        SourceSummary.Text = domains.Length == 0 ? "来源域名未记录" : string.Join("  ·  ", domains);
        TimeSummary.Text = report.LatestBrowserDownload is { } latest
            ? $"下载时间  {FormatTime(latest.CompletedAt)}"
            : report.PossibleBrowserDownloads.FirstOrDefault() is { } possible
                ? $"可能关联记录的下载时间  {FormatTime(possible.CompletedAt)}" : "";
        ResultStatus.Text = windows.Status switch
        {
            ProvenanceReadStatus.FileNotFound => "文件未找到",
            ProvenanceReadStatus.NotAFile => "请选择文件。当前路径是目录。",
            ProvenanceReadStatus.InvalidPath => "文件路径无效",
            ProvenanceReadStatus.AccessDenied => "读取失败。没有权限访问文件。",
            _ when report.HasConflict => "Windows 与浏览器的来源记录不一致",
            _ => report.Completeness switch
            {
                "sourceUrl" => "已找到来源",
                _ when windows.ReferrerUrl is not null || report.ReferrerUrl is not null => "仅记录了引用页面",
                "zoneOnly" => "仅有 Windows 区域，未记录来源网址",
                "possible" => "来源未确认。找到可能关联的浏览器记录。",
                "readFailed" => "读取失败，来源暂无法判断",
                _ => "来源未知"
            }
        };

        var windowsSummary = windows.Status switch
        {
            ProvenanceReadStatus.ReadFailed or ProvenanceReadStatus.AccessDenied => "读取失败",
            ProvenanceReadStatus.Read when windows.HasProvenance => "已读取",
            ProvenanceReadStatus.FileNotFound or ProvenanceReadStatus.NotAFile
                or ProvenanceReadStatus.InvalidPath => "文件不可用",
            _ => "未找到来源元数据"
        };
        if (windows.Zone is { } zone)
            windowsSummary += $" · {GetZoneName(zone.Id)} ({zone.Id})";
        if (windows.Issues.Count > 0)
            windowsSummary += $" · {windows.Issues.Count} 项元数据字段异常，可展开原始内容查看";
        WindowsEvidence.Content = new EvidenceView(windowsSummary,
            DescribeDomains(windows.SourceUrl, windows.ReferrerUrl), null, true,
            GetUrls(true, ("来源网址", windows.SourceUrl), ("引用页面", windows.ReferrerUrl)));

        BrowserEvidence.ItemsSource = report.BrowserDownloads.Select(download => DescribeBrowser(download, direct: true))
            .Concat(report.PossibleBrowserDownloads.Select(download => DescribeBrowser(download, direct: false))).ToArray();
        var browserStatus = report.BrowserError is not null ? "浏览器记录读取失败"
            : report.BrowserDownloads.Count + report.PossibleBrowserDownloads.Count == 0 ? "未找到关联记录" : "";
        if (report.HistoricalBrowserRecords > 0)
            browserStatus = $"原路径有 {report.HistoricalBrowserRecords} 条历史记录，可在「下载记录」查看。";
        BrowserStatus.Text = browserStatus;
        BrowserStatus.Visibility = browserStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        RawMetadataExpander.IsExpanded = false;
        RawMetadataText.Text = raw ?? "";
        RawMetadataExpander.Visibility = raw is null ? Visibility.Collapsed : Visibility.Visible;
        FilePathExpander.IsExpanded = false;
        FilePathText.Text = Path.IsPathFullyQualified(windows.Path)
            || windows.Status == ProvenanceReadStatus.InvalidPath ? windows.Path : Path.GetFullPath(windows.Path);
        EvidencePanel.Visibility = Visibility.Visible;
    }

    private static EvidenceView DescribeBrowser(StoredDownload download, bool direct)
    {
        var match = direct ? "直接匹配 · 路径、大小、修改时间和内容相符。同内容仍可能来自副本。"
            : download.MatchBasis == "sha256" ? "可能关联 · 内容相同"
            : "可能关联 · 路径、大小和修改时间相符，内容未经验证";
        var browser = download.Browser switch { "chrome" => "Chrome", "edge" => "Edge", _ => download.Browser };
        return new($"{match} · {browser} · {FormatTime(download.CompletedAt)}",
            DescribeDomains(download.FinalUrl ?? download.InitialUrl, download.ReferrerUrl), download.Path, false,
            GetUrls(direct, ("初始网址", download.InitialUrl), ("最终网址", download.FinalUrl),
                ("引用页面", download.ReferrerUrl)));
    }

    private static string DescribeDomains(string? source, string? referrer)
    {
        var sourceDomain = ProvenanceUrlSanitizer.GetHttpDomain(source);
        var referrerDomain = ProvenanceUrlSanitizer.GetHttpDomain(referrer);
        return string.Join("  ·  ", new[]
        {
            sourceDomain is null ? null : $"来源  {sourceDomain}",
            referrerDomain is null ? null : $"引用  {referrerDomain}"
        }.OfType<string>());
    }

    private static UrlField[] GetUrls(bool allowOpen, params (string Label, string? Value)[] fields) =>
        fields.Where(field => field.Value is not null)
            .Select(field => new UrlField(field.Label, field.Value!, allowOpen ? GetLaunchInfo(field.Value!) : null))
            .ToArray();

    private static ProcessStartInfo? GetLaunchInfo(string url)
    {
        try
        {
            return BrowserLauncher.CreateStartInfo(url);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UrlField field })
            return;
        try
        {
            Clipboard.SetText(field.Value);
            ActionStatus.Text = $"已复制{field.Label}";
        }
        catch (ExternalException)
        {
            ActionStatus.Text = "剪贴板不可用，请重试。";
        }
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: UrlField { LaunchInfo: { } info } })
            return;
        try
        {
            Process.Start(info);
            ActionStatus.Text = $"已请求打开 {ProvenanceUrlSanitizer.GetHttpDomain(info.FileName)}";
        }
        catch (Win32Exception)
        {
            ActionStatus.Text = "无法启动浏览器。";
        }
    }

    private static string FormatTime(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string GetZoneName(int id) => id switch
    {
        0 => "本地计算机", 1 => "本地 Intranet", 2 => "受信任的站点", 3 => "Internet", 4 => "受限制的站点",
        _ => "未知区域"
    };

    private sealed record EvidenceView(string Heading, string Summary, string? Path, bool IsWindows,
        IReadOnlyList<UrlField> Urls)
    {
        public bool HasPath => Path is not null;
        public bool HasDetails => HasPath || Urls.Count > 0;
        public string DetailsLabel => IsWindows ? "完整网址 (_U)" : "记录路径与完整网址";
    }

    private sealed record UrlField(string Label, string Value, ProcessStartInfo? LaunchInfo)
    {
        public bool CanOpen => LaunchInfo is not null;
        public string CopyLabel => $"复制{Label}";
        public string OpenLabel => $"打开 {ProvenanceUrlSanitizer.GetHttpDomain(Value)}";
    }
}
