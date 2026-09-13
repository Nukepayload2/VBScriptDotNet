# 脚本顶层 `Try`/`Catch`/`Finally` 里的 `Await` → 编译器 `NullReferenceException`（预期报 BC36943）

* 状态：**Open**
* 发现日期：2026-09-13
* 发现场景：调查「`.vbx` 顶层变量做成 locals」的可行性时顺带撞见

## 症状

`.vbx` 脚本顶层写含 `Await` 的 `Try`/`Catch`/`Finally`：

```vbx
Imports System.Threading.Tasks
Try
    Await Task.Delay(1)
    Console.WriteLine("TRY")
Catch ex As Exception
    Await Task.Delay(1)
    Console.WriteLine("CATCH")
Finally
    Await Task.Delay(1)
    Console.WriteLine("FINALLY")
End Try
```

实测（Debug `Scripting\VisualBasicTest\bin\Debug\net10.0\vbi.exe`，自报 `2.0.0-Beta+5816a5c`）：

```
System.NullReferenceException: Object reference not set to an instance of an object.
  + Microsoft.CodeAnalysis.CodeGen.ILBuilder.BlockedBranchDestinationSlow(...) — CodeGen\ILBuilder.cs : 413
  + ... MarkReachableFromBranch : 353 / MarkReachableFrom : 337 / MarkReachableBlocks : 276
  + ... RealizeBlocks : 870 / Realize : 207
  + Microsoft.CodeAnalysis.VisualBasic.CodeGen.CodeGenerator.GenerateImpl() — VisualBasic\Portable\CodeGen\CodeGenerator.vb : 172
  ...
The script has error. See the output for more information.
EXITCODE=3
```

**输出里没有任何编译诊断**——不是「报了错再崩」，是**直接崩**。

## 预期行为

应报 **`BC36943`**。该诊断确实存在，且在同一形状写成普通异步方法时正常工作（见下）。

## 判别性对照（**已运行**）

同一形状放进**普通异步方法**：

```vbx
Imports System.Threading.Tasks
Class C
    Async Function F1() As Task
        Try
            Await Task.Delay(1)
        Catch ex As Exception
            Await Task.Delay(1)
        Finally
            Await Task.Delay(1)
        End Try
    End Function
End Class
Console.WriteLine("COMPILED")
```

实测：

```
error BC36943: 「不能在『Catch』语句、『Finally』语句或『SyncLock』语句中使用『Await』。」(×2，指向 Catch 与 Finally 里那两处)
EXIT=1
```

⇒ **普通异步方法正常报 BC36943、不崩**；**脚本顶层崩**。**脚本模式特有**。

## 根因方向（**推测**）

`BC36943` 的判据在脚本顶层那条路上没有生效（或被绕过），含 `Await` 的 `Try`/`Finally` 因而**活到了发射期**，在共享的 `CodeGen.ILBuilder` 里撞空引用。

顶层代码住在合成的**异步宿主** `<Initialize>`（`MethodKind.Ordinary`、`IsShared=False`，见 `SynthesizedInteractiveInitializerMethod.vb`）里，与普通 `Async Function` 的检查路径不同；两侧在「发射前置的 `Await` 位置合法性检查」上**不一致**。

**未查**：`BC36943` 的判据落在哪个 binder 方法、为何对脚本宿主的顶层语句不生效。

## 相关

- 同批发现的姊妹问题：`issue-script-top-level-goto-await-crash.md`（同属「脚本顶层的不常规异步宿主 + 发射前置检查不一致」）。
- 该形状的「本该报错」性质与 `issue-script-shared-field-await-initializer-crash.md`（issue 06）同族——都是「VB 缺一条 `Await` 位置诊断 → 从报错变崩」。
