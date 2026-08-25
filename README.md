# App Window Host

一个 Windows WPF 工具，用于把外部 `.exe` 应用程序启动并嵌入到当前窗口中。

## 运行

```powershell
dotnet run --project .\AppWindowHost\AppWindowHost.csproj
```

启动后，可以点击“选择应用程序”，或直接把 `.exe` 文件拖到窗口下方的区域。

## 注意事项

- 目标应用必须创建可见的主窗口，命令行程序或托盘程序无法嵌入。
- 如果目标程序以管理员权限运行，而宿主没有管理员权限，Windows 可能阻止窗口嵌入。
- 关闭宿主窗口会请求关闭已嵌入的应用程序。