using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WhereFrom.Core;
using WhereFrom.Platform.Windows;
using WhereFrom.Storage;

namespace WhereFrom.App;

public partial class MainWindow : Window
{
    private readonly UnifiedProvenanceQuery query = new(
        new WindowsZoneProvider(), new DownloadStore(DownloadStore.DefaultPath));
    private bool isProcessing;
    private string? readWarning;
    private string? urlToOpen;

    public MainWindow() => InitializeComponent();

    public void OpenStartupFile(string path) => _ = InspectFileAsync(path);

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new OpenFileDialog { Filter = "所有文件|*.*", Multiselect = false };
            if (picker.ShowDialog(this) == true)
                await InspectFileAsync(picker.FileName);
        }
        catch (Exception ex)
        {
            ShowStatus($"无法选择文件：{ex.Message}", isError: true);
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
        {
            ShowStatus("正在读取，请稍候", isError: false);
            return;
        }

        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
                e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
            {
                ShowStatus("请拖入一个本地文件", isError: true);
                return;
            }
            if (paths.Length != 1)
            {
                ShowStatus("一次只能查询一个文件，请只拖入单个文件", isError: true);
                return;
            }
            if (Directory.Exists(paths[0]))
            {
                ShowStatus("不支持查询目录，请拖入单个文件", isError: true);
                return;
            }
            await InspectFileAsync(paths[0]);
        }
        catch (Exception ex)
        {
            ShowStatus($"无法读取拖入的文件：{ex.Message}", isError: true);
        }
    }

    private async Task InspectFileAsync(string filePath)
    {
        if (isProcessing)
            return;

        isProcessing = true;
        readWarning = null;
        urlToOpen = null;
        SelectFileButton.IsEnabled = false;
        ResultPanel.Visibility = Visibility.Visible;
        FileNameTextBox.Text = Path.GetFileName(filePath);
        FilePathTextBox.Text = filePath;
        SourceUrlTextBox.Clear();
        ReferrerUrlTextBox.Clear();
        ZoneTextBox.Clear();
        EvidenceTextBox.Clear();
        CopySourceButton.IsEnabled = false;
        CopyReferrerButton.IsEnabled = false;
        OpenSourceButton.IsEnabled = false;
        ShowStatus("正在读取…", isError: false);

        try
        {
            var result = await Task.Run(() => query.Inspect(filePath));
            DisplayResult(result);
        }
        catch (Exception ex)
        {
            ShowStatus($"读取失败：{ex.Message}", isError: true);
        }
        finally
        {
            isProcessing = false;
            SelectFileButton.IsEnabled = true;
        }
    }

    private void DisplayResult(ProvenanceReport result)
    {
        var windows = result.Windows;
        if (windows.Status is ProvenanceReadStatus.FileNotFound or ProvenanceReadStatus.AccessDenied
            or ProvenanceReadStatus.NotAFile or ProvenanceReadStatus.InvalidPath
            || windows.Status == ProvenanceReadStatus.ReadFailed && result.BrowserDownloads.Count == 0)
        {
            string errorMessage = windows.Status switch
            {
                ProvenanceReadStatus.FileNotFound => "文件未找到",
                ProvenanceReadStatus.AccessDenied => "权限不足，无法访问该文件",
                ProvenanceReadStatus.NotAFile => "提供的路径是一个目录，请选择文件",
                ProvenanceReadStatus.InvalidPath => "文件路径无效",
                ProvenanceReadStatus.ReadFailed => $"读取失败：{windows.Error ?? "未知错误"}",
                _ => "未知错误"
            };
            ShowStatus(errorMessage, isError: true);
            return;
        }

        SourceUrlTextBox.Text = result.SourceUrl ?? "未记录";
        ReferrerUrlTextBox.Text = result.ReferrerUrl ?? "未记录";
        CopySourceButton.IsEnabled = result.SourceUrl is not null;
        CopyReferrerButton.IsEnabled = result.ReferrerUrl is not null;
        ZoneTextBox.Text = windows.Zone is null ? "未记录"
            : windows.Zone.Name is { Length: > 0 } name
                ? $"{name} ({windows.Zone.Id})" : $"未知区域 ({windows.Zone.Id})";

        var evidence = new List<string>();
        if (windows.HasProvenance)
        {
            evidence.Add("Windows Zone.Identifier");
            if (windows.SourceUrl is not null)
                evidence.Add($"  来源：{windows.SourceUrl}（原始地址，可能含敏感参数）");
            if (windows.ReferrerUrl is not null)
                evidence.Add($"  引用：{windows.ReferrerUrl}");
        }
        foreach (var download in result.BrowserDownloads)
        {
            evidence.Add(download.MatchBasis == "sha256"
                ? $"{download.Browser} 下载记录（路径、大小、时间与 SHA-256 一致；仍可能是同内容副本）"
                : $"{download.Browser} 下载记录（仅路径、大小和时间一致；内容未经验证）");
            if ((download.FinalUrl ?? download.InitialUrl) is { } source)
                evidence.Add($"  来源：{source}");
            if (download.ReferrerUrl is not null)
                evidence.Add($"  引用：{download.ReferrerUrl}");
        }
        foreach (var download in result.PossibleBrowserDownloads)
        {
            evidence.Add(download.MatchBasis == "sha256"
                ? $"可能关联的 {download.Browser} 下载记录（SHA-256 内容一致；可能是移动或复制）"
                : $"可能关联的 {download.Browser} 下载记录（仅路径、大小和时间一致；内容未经验证）");
            evidence.Add($"  原记录路径：{download.Path}");
            if ((download.FinalUrl ?? download.InitialUrl) is { } source)
                evidence.Add($"  可能来源：{source}");
            if (download.ReferrerUrl is not null)
                evidence.Add($"  可能引用：{download.ReferrerUrl}");
        }
        if (result.HasConflict)
            evidence.Add("Windows 与浏览器来源网址不同；两条证据均已保留。");
        if (result.HistoricalBrowserRecords > 0)
            evidence.Add("此路径的旧浏览器记录因文件变化未用于当前查询。");
        EvidenceTextBox.Text = evidence.Count == 0 ? "未找到证据" : string.Join(Environment.NewLine, evidence);

        urlToOpen = result.ReferrerUrl ?? result.SourceUrl;
        if (urlToOpen is not null)
        {
            try
            {
                BrowserLauncher.CreateStartInfo(urlToOpen);
                OpenSourceButton.IsEnabled = true;
            }
            catch (ArgumentException)
            {
                urlToOpen = null;
            }
        }

        var warnings = new List<string>();
        if (windows.Status == ProvenanceReadStatus.ReadFailed)
            warnings.Add("Windows 元数据读取失败");
        if (result.BrowserError is not null)
            warnings.Add("浏览器记录读取失败");
        if (result.HasConflict)
            warnings.Add("Windows 与浏览器来源不一致");
        foreach (var issue in windows.Issues)
            warnings.Add($"{GetFieldName(issue.Field)}：{GetIssueMessage(issue.Kind)}");
        readWarning = warnings.Count > 0 ? string.Join("；", warnings) : null;

        if (!result.HasProvenance)
            ShowStatus(readWarning ?? "未找到来源信息", isError: readWarning is not null);
        else if (readWarning is not null)
            ShowStatus(readWarning, isError: false);
        else if (result.Completeness == "possible")
            ShowStatus("找到可能关联的下载记录，无法确认是同一文件", isError: false);
        else if (result.Completeness == "zoneOnly")
            ShowStatus("仅记录了 Windows 区域，未记录来源地址", isError: false);
        else
            ShowStatus("已读取文件来源信息", isError: false);
    }

    private void CopySourceButton_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(SourceUrlTextBox.Text, "下载地址");

    private void CopyReferrerButton_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(ReferrerUrlTextBox.Text, "引用页面");

    private void CopyToClipboard(string value, string fieldName)
    {
        try
        {
            Clipboard.SetText(value);
            ShowStatus($"{fieldName}已复制到剪贴板" +
                (readWarning is null ? "" : $"；{readWarning}"), isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"复制失败：{ex.Message}", isError: true);
        }
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (urlToOpen is null)
        {
            ShowStatus("没有可打开的来源地址", isError: true);
            return;
        }
        try
        {
            Process.Start(BrowserLauncher.CreateStartInfo(urlToOpen));
            var displayUrl = urlToOpen.Length > 60 ? urlToOpen[..57] + "..." : urlToOpen;
            ShowStatus($"正在打开：{displayUrl}" +
                (readWarning is null ? "" : $"；{readWarning}"), isError: false);
        }
        catch (ArgumentException)
        {
            ShowStatus("来源地址不是有效的 HTTP/HTTPS 地址，无法打开", isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"无法启动浏览器：{ex.Message}", isError: true);
        }
    }

    private static string GetFieldName(ProvenanceField field) => field switch
    {
        ProvenanceField.Metadata => "元数据",
        ProvenanceField.Zone => "区域标识",
        ProvenanceField.SourceUrl => "下载地址",
        ProvenanceField.ReferrerUrl => "引用页面",
        _ => field.ToString()
    };

    private static string GetIssueMessage(ProvenanceIssueKind kind) => kind switch
    {
        ProvenanceIssueKind.EmptyValue => "值为空",
        ProvenanceIssueKind.InvalidValue => "值无效",
        ProvenanceIssueKind.DuplicateField => "字段重复",
        ProvenanceIssueKind.InvalidFormat => "格式无效",
        ProvenanceIssueKind.MissingSection => "缺少节",
        _ => kind.ToString()
    };

    private void ShowStatus(string message, bool isError)
    {
        StatusTextBlock.Text = message;
        StatusBorder.Visibility = Visibility.Visible;
        StatusBorder.Background = isError
            ? new SolidColorBrush(Color.FromRgb(255, 225, 225))
            : new SolidColorBrush(Color.FromRgb(255, 244, 212));
    }
}
