using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WhereFrom.Core;
using WhereFrom.Platform.Windows;

namespace WhereFrom.App;

public sealed partial class MainWindow : Window
{
    private readonly WindowsZoneProvider provider = new();
    private bool isProcessing = false;

    public MainWindow()
    {
        InitializeComponent();
        Title = "WhereFrom";

        // 设置初始窗口尺寸
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(760, 520));
    }

    private async void SelectFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.ViewMode = PickerViewMode.List;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            await InspectFileAsync(file.Path);
        }
    }

    private void ContentGrid_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "拖放以查看文件来源";
    }

    private async void ContentGrid_Drop(object sender, DragEventArgs e)
    {
        if (isProcessing)
        {
            ShowStatus("正在读取，请稍候", isError: false);
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var items = await e.DataView.GetStorageItemsAsync();

            if (items.Count == 0)
            {
                ShowStatus("未检测到有效文件", isError: true);
                return;
            }

            if (items.Count > 1)
            {
                ShowStatus("一次只能查询一个文件，请只拖入单个文件", isError: true);
                return;
            }

            var item = items[0];
            if (item is StorageFolder)
            {
                ShowStatus("不支持查询目录，请拖入单个文件", isError: true);
                return;
            }

            if (item is StorageFile file)
            {
                await InspectFileAsync(file.Path);
            }
        }
    }

    private async Task InspectFileAsync(string filePath)
    {
        if (isProcessing)
        {
            return;
        }

        isProcessing = true;
        SelectFileButton.IsEnabled = false;

        // 显示正在读取状态
        InitialPrompt.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;

        FileNameTextBox.Text = Path.GetFileName(filePath);
        FilePathTextBox.Text = filePath;
        SourceUrlTextBox.Text = "";
        ReferrerUrlTextBox.Text = "";
        ZoneTextBox.Text = "";

        CopySourceButton.IsEnabled = false;
        CopyReferrerButton.IsEnabled = false;
        OpenSourceButton.IsEnabled = false;

        ShowStatus("正在读取…", isError: false);

        try
        {
            // 在后台线程读取文件
            var result = await Task.Run(() => provider.Inspect(filePath));

            // 在 UI 线程更新结果
            DispatcherQueue.TryEnqueue(() => DisplayResult(result));
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ShowStatus($"读取失败：{ex.Message}", isError: true);
            });
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                isProcessing = false;
                SelectFileButton.IsEnabled = true;
            });
        }
    }

    private void DisplayResult(ProvenanceResult result)
    {
        // 处理错误状态
        if (result.Status != ProvenanceReadStatus.Read)
        {
            string errorMessage = result.Status switch
            {
                ProvenanceReadStatus.FileNotFound => "文件未找到",
                ProvenanceReadStatus.AccessDenied => "权限不足，无法访问该文件",
                ProvenanceReadStatus.NotAFile => "提供的路径是一个目录，请选择文件",
                ProvenanceReadStatus.InvalidPath => "文件路径无效",
                ProvenanceReadStatus.ReadFailed => $"读取失败：{result.Error ?? "未知错误"}",
                ProvenanceReadStatus.NoMetadata => "未找到来源信息",
                _ => "未知错误"
            };

            ShowStatus(errorMessage, isError: true);
            return;
        }

        // 显示 URL 字段
        if (!string.IsNullOrEmpty(result.SourceUrl))
        {
            SourceUrlTextBox.Text = result.SourceUrl;
            CopySourceButton.IsEnabled = true;
        }
        else
        {
            SourceUrlTextBox.Text = "未记录";
        }

        if (!string.IsNullOrEmpty(result.ReferrerUrl))
        {
            ReferrerUrlTextBox.Text = result.ReferrerUrl;
            CopyReferrerButton.IsEnabled = true;
        }
        else
        {
            ReferrerUrlTextBox.Text = "未记录";
        }

        // 显示 Zone 信息
        if (result.Zone != null)
        {
            if (!string.IsNullOrEmpty(result.Zone.Name))
            {
                ZoneTextBox.Text = $"{result.Zone.Name} ({result.Zone.Id})";
            }
            else
            {
                ZoneTextBox.Text = $"未知区域 ({result.Zone.Id})";
            }
        }
        else
        {
            ZoneTextBox.Text = "未记录";
        }

        // 设置打开按钮状态
        string? urlToOpen = result.ReferrerUrl ?? result.SourceUrl;
        if (!string.IsNullOrEmpty(urlToOpen))
        {
            try
            {
                BrowserLauncher.CreateStartInfo(urlToOpen);
                OpenSourceButton.IsEnabled = true;
            }
            catch
            {
                OpenSourceButton.IsEnabled = false;
            }
        }

        // 显示状态信息
        if (!result.HasProvenance)
        {
            if (result.Zone != null && string.IsNullOrEmpty(result.SourceUrl) && string.IsNullOrEmpty(result.ReferrerUrl))
            {
                ShowStatus("仅记录了 Windows 区域，未记录来源地址", isError: false);
            }
            else
            {
                ShowStatus("未找到来源信息", isError: false);
            }
        }
        else
        {
            // 显示警告信息
            if (result.Issues.Count > 0)
            {
                string warnings = "部分字段存在问题：\n";
                foreach (var issue in result.Issues)
                {
                    string message = GetIssueMessage(issue.Kind);
                    warnings += $"• {GetFieldName(issue.Field)}: {message}\n";
                }
                ShowStatus(warnings.TrimEnd(), isError: false);
            }
            else
            {
                ShowStatus("已读取文件来源信息", isError: false);
            }
        }
    }

    private void CopySourceButton_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(SourceUrlTextBox.Text, "下载地址");
    }

    private void CopyReferrerButton_Click(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(ReferrerUrlTextBox.Text, "引用页面");
    }

    private void CopyToClipboard(string text, string fieldName)
    {
        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);

            ShowStatus($"{fieldName}已复制到剪贴板", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"复制失败：{ex.Message}", isError: true);
        }
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        string? urlToOpen = ReferrerUrlTextBox.Text != "未记录" ? ReferrerUrlTextBox.Text : null;
        urlToOpen ??= SourceUrlTextBox.Text != "未记录" ? SourceUrlTextBox.Text : null;

        if (string.IsNullOrEmpty(urlToOpen))
        {
            ShowStatus("没有可打开的来源地址", isError: true);
            return;
        }

        try
        {
            var startInfo = BrowserLauncher.CreateStartInfo(urlToOpen);
            System.Diagnostics.Process.Start(startInfo);

            string openedUrl = urlToOpen.Length > 60 ? urlToOpen.Substring(0, 57) + "..." : urlToOpen;
            ShowStatus($"正在打开：{openedUrl}", isError: false);
        }
        catch (ArgumentException)
        {
            string fieldName = ReferrerUrlTextBox.Text != "未记录" ? "引用页面" : "下载地址";
            ShowStatus($"{fieldName}不是有效的 HTTP/HTTPS 地址，无法打开", isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus($"无法启动浏览器：{ex.Message}", isError: true);
        }
    }

    private string GetFieldName(ProvenanceField field)
    {
        return field switch
        {
            ProvenanceField.Metadata => "元数据",
            ProvenanceField.Zone => "区域标识",
            ProvenanceField.SourceUrl => "下载地址",
            ProvenanceField.ReferrerUrl => "引用页面",
            _ => field.ToString()
        };
    }

    private string GetIssueMessage(ProvenanceIssueKind kind)
    {
        return kind switch
        {
            ProvenanceIssueKind.EmptyValue => "值为空",
            ProvenanceIssueKind.InvalidValue => "值无效",
            ProvenanceIssueKind.DuplicateField => "字段重复",
            ProvenanceIssueKind.InvalidFormat => "格式无效",
            ProvenanceIssueKind.MissingSection => "缺少节",
            _ => kind.ToString()
        };
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusTextBlock.Text = message;
        StatusBorder.Visibility = Visibility.Visible;

        if (isError)
        {
            StatusBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBackgroundBrush"];
        }
        else
        {
            StatusBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"];
        }
    }
}
