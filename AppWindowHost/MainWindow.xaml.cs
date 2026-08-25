using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace AppWindowHost;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private Process? hostedProcess;
    private IntPtr hostedWindowHandle;

    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsMinimizeBox = 0x00020000;
    private const int WsMaximizeBox = 0x00010000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsChild = 0x40000000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;

    public MainWindow()
    {
        InitializeComponent();
        HostSurface.SizeChanged += (_, _) => ResizeHostedWindow();
        Closed += (_, _) => CloseHostedProcess();
    }

    private void ChooseApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "应用程序 (*.exe)|*.exe",
            Title = "选择要嵌入的应用程序"
        };

        if (dialog.ShowDialog() == true)
        {
            HostApplication(dialog.FileName);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasExecutableFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
        {
            HostApplication(files[0]);
        }

        e.Handled = true;
    }

    private static bool HasExecutableFile(IDataObject data)
    {
        return data.GetDataPresent(DataFormats.FileDrop)
            && data.GetData(DataFormats.FileDrop) is string[] files
            && files.Length > 0
            && Path.GetExtension(files[0]).Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private async void HostApplication(string path)
    {
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "只能嵌入 .exe 应用程序";
            return;
        }

        CloseHostedProcess();
        EmptyState.Visibility = Visibility.Collapsed;
        StatusText.Text = $"正在启动 {Path.GetFileName(path)}...";

        try
        {
            var applicationDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
            var workingDirectory = Directory.GetParent(applicationDirectory)?.FullName ?? applicationDirectory;
            hostedProcess = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = workingDirectory
            });
            if (hostedProcess is null)
            {
                throw new InvalidOperationException("无法启动应用程序。");
            }

            for (var attempt = 0; attempt < 50 && hostedProcess.MainWindowHandle == IntPtr.Zero; attempt++)
            {
                await Task.Delay(100);
                hostedProcess.Refresh();
            }

            hostedWindowHandle = hostedProcess.MainWindowHandle;
            if (hostedWindowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("应用程序没有可嵌入的主窗口。");
            }

            SetWindowLong(hostedWindowHandle, GwlStyle, (GetWindowLong(hostedWindowHandle, GwlStyle) & ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsPopup)) | WsChild);
            SetParent(hostedWindowHandle, new System.Windows.Interop.WindowInteropHelper(this).Handle);
            ResizeHostedWindow();
            StatusText.Text = $"已嵌入：{Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            CloseHostedProcess();
            EmptyState.Visibility = Visibility.Visible;
            StatusText.Text = $"启动失败：{exception.Message}";
        }
    }

    private void ResizeHostedWindow()
    {
        if (hostedWindowHandle == IntPtr.Zero || !HostSurface.IsLoaded)
        {
            return;
        }

        var point = HostSurface.TransformToAncestor(this).Transform(new System.Windows.Point(0, 0));
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformToDevice ?? new System.Windows.Media.Matrix(1, 0, 0, 1, 0, 0);
        var x = (int)Math.Round(point.X * transform.M11);
        var y = (int)Math.Round(point.Y * transform.M22);
        var width = (int)Math.Round(HostSurface.ActualWidth * transform.M11);
        var height = (int)Math.Round(HostSurface.ActualHeight * transform.M22);

        SetWindowPos(hostedWindowHandle, IntPtr.Zero, x, y, Math.Max(0, width), Math.Max(0, height), SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
    }

    private void CloseHostedProcess()
    {
        if (hostedProcess is { HasExited: false })
        {
            hostedProcess.CloseMainWindow();
            hostedProcess.Dispose();
        }

        hostedProcess = null;
        hostedWindowHandle = IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr childHandle, IntPtr parentHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);

    private static int GetWindowLong(IntPtr handle, int index) => unchecked((int)GetWindowLongPtr(handle, index).ToInt64());

    private static void SetWindowLong(IntPtr handle, int index, int value) => SetWindowLongPtr(handle, index, new IntPtr(value));
}