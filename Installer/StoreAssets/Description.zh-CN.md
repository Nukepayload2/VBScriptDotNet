此应用程序是 Nukepayload2 对 Visual Basic 交互式编译器的修改版。
它可以直接执行源代码片段，从而将 Visual Basic .NET 用作脚本语言。
你可以通过编写 VB 代码访问 .NET 8、Windows 窗体、WPF 和部分 WinUI API，而无需创建 VB 项目。
它还可以运行 *.vbx 文件，这是 *.vbs (VBScript) 文件的现代 .NET 替代文件格式。在通过文件关联运行脚本文件之前，您需要确认安全警告。
目前这个软件的中文内容不完整，有一部分是英文的。
请注意，这是一款预发布软件。在报告错误之前，请从项目 URL 中查看已知问题。
脚本运行时的技术信息：
- Visual Basic 版本：16.9
- Visual Basic 编译器： Roslyn 4.7.0，带有一些用于运行脚本的补丁
- 嵌入的 .NET 运行时：.NETCore.App 8.0.0 和 WindowsDesktop.App 8.0.0
- 脚本宿主的目标框架：net8.0-windows10.0.17763.0 或者 .NET Framework 4.8