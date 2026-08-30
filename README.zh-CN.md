# VBScriptDotNet

VBScriptDotNet 是 Roslyn Visual Basic 编译器的 fork，随附命令行交互宿主（`vbi`）和 `.vbx` 脚本运行器，无需项目即可直接运行 Visual Basic 代码。`.vbx` 是 `.vbs` 的现代 .NET 替代品。

当前开发版本：**2.0.0-Beta**。以下功能描述适用于此开发版本。

## 功能

### 交互窗口
- REPL：`>` 提示符、多行续行、以 VB 格式打印值。
- 顶层代码免包装：`Dim`、`Sub`、`Function`、`Class` 和 `Module`。
- 顶层 `Await`、`AddHandler` 和 `RemoveHandler`。
- `Imports` 跨提交累积。
- `?` 打印指令是隐式的：提交末尾的值表达式无需前缀即以 VB 格式打印其值，与 C# 交互窗口一致。例外是 `=`：不写 `?` 时 `x = 5` 是赋值；写 `?` 时 `? x = 5` 是比较，两种写法影响运算符重载决策。
- 指令：`#R`、`#Load`、`#help`、`/help`、`/version` 和 `/?`。

### 脚本文件（.vbx）
- 用 `vbi script.vbx [-- args]` 运行脚本。
- 在文件顶部用 `' Attribute TargetFramework = "net48"` 选择运行时宿主。
- 脚本顶部的 `#!` shebang 被视为指令，可在类 Unix 系统上直接运行脚本。
- 脚本全局对象：`Args` 和 `Print`。
- 用 `vbi.rsp` 配置脚本编译（`/r:`、`/imports:`）。

### C# 互操作
- 消费 C# 14 扩展成员，包括扩展属性和扩展运算符。
- 通过约束到接口的类型参数调用静态抽象接口成员，如 `T.Zero` 和 `T.Add`。
- 使用 `Span(Of T)` 等 byref-like 类型，并在泛型参数允许 ref struct 时将其作为类型实参传递。

### 脚本工具
- `/check` 编译脚本并报告全部诊断信息，但不执行。
- `/optimize+` 为脚本编译启用发布优化，默认仍为 Debug。

## 示例

### #R 指令
以下代码调用 Windows NuGet 包缓存中的 `Newtonsoft.Json 13.0.3`，将数字序列化为 JSON，并以 VB 格式打印该值。
```vbnet
#R "C:\Users\<your user name>\.nuget\packages\newtonsoft.json\13.0.3\lib\net10.0\Newtonsoft.Json.dll"
Newtonsoft.Json.JsonConvert.SerializeObject(1)
```

### ? 指令
以 VB 格式打印值。
以下代码根据所使用的操作系统打印 `vbCrLf` 或 `vbLf`。
```vbnet
? Environment.NewLine
```

`?` 前缀对大多数表达式是可选的；提交末尾的裸值表达式会自动打印。例外是 `=`：不写 `?` 时 `x = 5` 是赋值；写 `?` 时 `? x = 5` 是比较。两种写法选择不同的运算符，因此影响运算符重载决策。
```vbnet
Dim x = 1
? x = 5        ' False
x = 5          ' 赋值，不打印
? x            ' 5
```

### 顶层代码
可以直接使用 `Dim`、`Sub` 和 `Function`，无需显式包装。
以下代码在不声明类或模块的情况下打印斐波那契数列。
```vbnet
Function Fibonacci(n As Integer) As Integer
    If n <= 1 Then
        Return n
    Else
        Return Fibonacci(n - 1) + Fibonacci(n - 2)
    End If
End Function

Sub PrintFibonacci(count As Integer)
    Console.WriteLine(String.Join(",",
        From i In Enumerable.Range(1, count)
        Select Fibonacci(i)))
End Sub

Dim count = 10
PrintFibonacci(count)
```

### 特性注释
在 `vbx` 脚本文件顶部用特殊注释指定项目属性。
#### 语法
```vbnet
' Attribute <property-name> = <value-constant-expression>
```
#### TargetFramework 特性注释
该特性用于按文件关联运行脚本时选择 `vbx` 文件的脚本运行器。
脚本运行器默认使用 .NET 运行时。
可通过以下注释选择 .NET Framework 脚本运行器：
```vbnet
' Attribute TargetFramework = "net48"
```

示例：[使用 .NET Framework 的 Excel](Samples/ExcelWithNetFramework.vbx)

## 安装
<a href="ms-windows-store://pdp/?ProductId=9N210C9TDZ95&mode=mini">
   <img src="https://get.microsoft.com/images/en-us%20dark.svg" alt="Download VB Interactive" />
</a>

Microsoft Store 版为 1.2。本仓库的当前开发版本为 2.0.0-Beta。

## 运行方法

### 使用 Visual Studio 运行
- 确保已安装最新的 Visual Studio 2022、.NET 桌面工作负载和 .NET 10 SDK。
- 打开 `VBInteractive.sln`。
- 将 [vbi](Interactive\vbi\vbi.vbproj) 设为启动项目。
- 将目标框架改为 `net10.0-windows`。
- 运行。

### 使用 .NET SDK 运行
- cd `Interactive\vbi`
- 交互式运行：`dotnet run --framework net10.0`
- 在 Windows 上交互式运行：`dotnet run --framework net10.0-windows`
- 脚本模式运行：`dotnet run --framework net10.0 -- <path-to-vbx-file>`
- 在 Windows 上脚本模式运行：`dotnet run --framework net10.0-windows -- <path-to-vbx-file>`

## 从源码构建

安装 .NET 10 SDK，然后在仓库根目录构建解决方案：

```
dotnet build VBInteractive.sln
```

## 已知问题

- `.vbx` 文件无法使用原始版本的 Roslyn 运行；它们需要本仓库随附的编译器。

更多信息，请参阅 https://github.com/Nukepayload2/VBScriptDotNet/issues

## 许可证

本项目基于 [MIT 许可证](License.txt)。
