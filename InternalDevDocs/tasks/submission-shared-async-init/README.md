# 任务：脚本共享挂钩的"初始化时机"要么实测保证、要么改成显式幂等异步初始化

（任务文件夹名：`submission-shared-async-init`。缺陷登记：`..\..\issues\issue-submission-shared-init-timing-unverified.md`（issue 36）——给人看的，做任务读这个文件就够。背景另见 `..\submission-shared-handles-hookup\`（已收口的 18-B）与队列 #9/#13。）

## 1. 要解决的问题，大白话

上个任务让脚本里 `Shared Sub H(...) Handles Me.SharedEvent` 能用了：做法是给提交类补一个静态构造函数（`.cctor`），把 `AddHandler` 塞进去。

问题是：**"这个静态构造函数会在需要之前跑完"这件事，现在靠的是 CLR 的一条元数据规则，而且没人验证过它真的生效。**

CLR 规则：类型上如果带 `BeforeFieldInit` 这个标记位，静态构造函数就是"惰性"的——CLR 可以在任意时点跑，一般到第一次访问静态**字段**才跑；如果不带这个位（精确语义），才能保证"访问任何静态成员（包括调用静态方法）之前必须先跑完"。

所以：
- 如果这个位确实被清掉了 ⇒ 现在的做法是站得住的，只是缺回归钉；
- 如果这个位没被清掉 ⇒ 只要有人"只调一个静态方法、不碰任何静态字段"，挂钩就可能还没挂上，`RaiseEvent` 静默丢失，而且这种时序在测试里可能永远碰不上。

作者另外指出两件结构性问题：静态构造函数**不能用 `Await`**（而脚本主体本来就是异步的），以及"同一个事件被挂两次/漏挂"现在没有显式的"只挂一次"保证。

## 2. 别踩的坑：现有那条"投递成功"的测试证明不了时机

现在跑的是这样一段（`SubmissionSharedHandlesHookupTests.vb:52-56`）：

```
Shared Sub Fire()
    RaiseEvent Ev(Nothing, System.EventArgs.Empty)
End Sub
Fire()
System.Console.Write(Sink.Count())
```

它看着"挂钩生效了"，其实什么都证明不了，三个理由：
1. `Fire()` 和 `Sink.Count()` 都只是**调用静态方法**——只有"不带 `BeforeFieldInit`"才保证静态方法调用会触发初始化；
2. 被读的静态字段在**别的类型 `Sink`** 上，只会触发 `Sink` 自己的初始化，碰不到提交类；
3. 就算这个位没清掉，CLR 也**允许**提前跑 ⇒ 两种情况下这条测试都可能绿。

⇒ 别拿它当证据，也别为了"让它更严"去改它；要新增能鉴别的格子。

## 3. 现成可用的工具（不用造轮子）

- 读属性位：`Compilers\VisualBasicEmitTest\Emit\EmitMetadata.vb:605`（`EmitBeforeFieldInit`）＋ `:646`、`:678` 用 `Assert.Equal(TypeAttributes.BeforeFieldInit Or TypeAttributes.Public, row.Attributes)`；
- 语义层判断：`Compilers\VisualBasicSemanticTest\Semantics\FieldInitializerBindingTests.vb:222` 的 `Assert.False(IsBeforeFieldInit(typeSymbol))`；
- 发射侧判据的源头：`Compilers\VisualBasic\Portable\Emit\NamedTypeSymbolAdapter.vb:475-499`（三条分支，issue 36 §一.1 抄了原文）。

## 4. 按顺序做

**第一步：只做测量，不改产品代码。** 跑 issue 36 §四 的 T1/T2/T3/T4 四格，逐格贴原始读数。T1、T2 是核心：**T1 看位、T2 看"只调静态方法"时到底挂没挂上**，两者要交叉成四种组合都记录。

**第二步：按读数分叉。**
- 若 T1 显示位已清、T2 显示时序正确 ⇒ 走**甲**：不改行为，补 3 条回归钉（属性位、只调静态方法的投递、跨提交恰好一次），并在规范里把这条承诺写清（先英文正本，再中文版）。
- 若 T1 位没清、或 T2/T4 显示不可靠 ⇒ **停手回报**。因为这会引出**乙**：把共享初始化改成显式的"首次调用者启动、其他人等同一个任务、重复调用只启动一次"的门（`Lazy(Of Task)` 那类），`.cctor` 只留 CLR 允许它做的事。乙 要改语义、要新设计文档，不由子任务自己拍板。

**第三步（甲路线才有）：回归 + 记账。**

## 5. 硬约束

- **不许改 `NamedTypeSymbolAdapter.vb:475-499` 这段判据本身**，除非第二步的结论要求并且另行立项。它对所有类和模块生效。
- 任何后续改动都必须带**普通编译对照**：同形状的普通 `Class`（带/不带显式 `Sub New`、带 `Handles`）与 `Module`（只有字段初始化器）发射出来的属性位**逐位不变**。这条是"不影响普通 VB"的硬规矩（`..\..\decisions.md` **D10**）。
- 不许把现有的 `SubmissionSharedHandlesHookupTests` 格子改弱或删掉。
- 禁止任何 git 写操作（`git add`/`commit`/`stash`/`checkout`/`restore`）。全量回归（七个门、整套脚本测试）不归子任务跑，子任务只跑定向过滤。

## 6. 回报要写什么

每一步：目标 / 实际做了什么 / 改了哪些文件或得到什么结论 / 通过还是打回；每条结论标**跑出来的 / 读代码确认的 / 猜的**；猜的附上怎么验证。日志写 `tmp\vortex-logs\submission-shared-async-init\{步骤号}-{角色}-{简述}.md`，开工先读同目录 `pitfalls.md`。
