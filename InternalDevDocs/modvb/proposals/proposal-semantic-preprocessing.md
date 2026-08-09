# 语义预处理 / Semantic Pre-Processing (`##If`)

* [x] Proposed
* [ ] Prototype: [Complete](https://github.com/PROTOTYPE_OWNER/roslyn/BRANCH_NAME)
* [ ] Implementation: [In Progress](https://github.com/dotnet/roslyn/BRANCH_NAME)
* [ ] Specification: [Not Started](pr/1)

## Summary
[summary]: #summary

本建议引入**语义预处理指令** `##If`/`##ElseIf`/`##Else`/`##End If`：条件编译不再只基于编译符号，而是基于**语义**——`METHOD_EXISTS`、`TYPE_EXISTS`、`MEMBER_EXISTS` 等内建谓词检查类型、成员、方法是否存在，据此编译语句或声明。`##If` 内部还可配合现有 `#Warning` / `#Error` 指令向项目模板与源码生成器报告问题。

## Motivation
[motivation]: #motivation

跨版本、跨平台的版本化场景长期是痛点：同一份共享代码/链接文件要适配不同框架版本与平台，若按编译符号手工管理条件编译，符号散落各处且与实际 API 存在与否脱节。语义预处理让条件基于"该 API 在目标框架/平台是否存在"，实现共享项目的"自适应点灯"（adaptive light-up）与跨平台代码共享。

期望的结果：

- 语句可按语义条件编译（如某方法存在才调用、否则退回旧方法）；
- 声明可按语义条件编译（如目标平台存在某类型才声明对应重载）；
- 源码生成器可用它检测"成员缺失"并自动补样板，避免重复声明；
- 模板/生成器可在 `##If` 内发出 `#Warning` / `#Error`。

## Detailed design
[design]: #detailed-design

### 条件编译语句

`##If METHOD_EXISTS(...)` 判断对象上是否存在某方法，据此选择语句：

```vb
' Conditionally compile statements.

' Enables better code-sharing (e.g. shared projects/linked-files)
' and cross-platform "adaptive light-up".
Let obj As MyType = ...

##If METHOD_EXISTS(obj.BetterMethod) Then
    obj.BetterMethod(...)
##Else
    ' Must call obsolete method in this case.
    #Ignore Warning BC40008
    obj.OldSlowMethod(...)
##End If
```

语义存在时调用 `BetterMethod`，否则退回被标记废弃（`#Ignore Warning` 抑制 BC40008）的旧方法——比依赖符号的 `#If` 更贴合真实 API 面。

### 条件编译声明

`##If TYPE_EXISTS(...)` 判断目标平台是否存在某类型，据此声明成员：

```vb
' Conditionally compile declarations.

' Enables source generation from syntax w/o requiring semantic
' analysis.
##If TYPE_EXISTS(System.DateTimeOffset) Then
    Sub WriteLine(value As DateTimeOffset)
        ...
    End Sub
##End If
```

Anthony 注明：这使源码生成可以仅凭语法（syntax）而不依赖语义分析（semantic analysis）——生成器先写语义判断，编译器再决定保留哪些声明。

### 源码生成器的样板补充

`##If Not MEMBER_EXISTS(...)` 用于"若开发者未声明，则补充样板"：

```vb
' Enables source generators to provide boilerplate if missing.

' ToolGenerated.vb
Partial Class MyViewModel
    Implements INotifyPropertyChanged

    ' Only declare this if the end-developer didn't.
    ##If Not MEMBER_EXISTS(PropertyChanged) Then
        Event PropertyChanged As PropertyChangedEventHandler
    ##End If

End Class

##If MEMBER_EXISTS(OnNameChanged) Then
    OnNameChanged()

##ElseIf MEMBER_EXISTS(OnPropertyChanged) Then
    OnPropertyChanged("Name")

##Else
    ' Derived types can't raise base class events!
    RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs("Name"))
##End If
```

支持 `##ElseIf` 链：优先调用开发者已定义的 `OnNameChanged`，其次 `OnPropertyChanged`，都没有才直接 `RaiseEvent`。由于派生类型无法直接触发基类事件，最后一种情况下仍可发出事件。

### `##If` 内的 `#Warning` / `#Error`

项目模板与源码生成器可在 `##If` 分支内发出诊断：

```vb
' Enables project templates/source generators to detect
' errors or provide warnings.

##If ... Then
    #Warning "Behavior may be different on this platform. Did you mean to target .NET 11?"
##End If

##If Not TYPE_EXISTS(...) Then
    #Warning "Types from 'MyAssembly' required at runtime via reflection. Add a reference to ensure binary is deployed on build."
##End If

##If Not MEMBER_EXISTS(...) Then
    #Error "Must define a deserialization constructor."
#End If
```

例如：平台行为可能不同时给 `#Warning`；反射所需类型缺失时提示"添加引用以保证生成二进制被部署"；缺少反序列化构造函数时给 `#Error`。

## Drawbacks
[drawbacks]: #drawbacks

- 语义条件编译高度依赖分析器/源码生成器技术的性能——Anthony 明确说明本能力"高度依赖某些分析器或源码生成器技术的性能"。
- `##If` 中涉及语义检查（`TYPE_EXISTS` 等）时，编译需先进行部分绑定，使预处理阶段变复杂，也与现有 `#If` 纯文本宏的简单模型分叉。
- 谓词名（`obj.BetterMethod` 这样的带对象表达式）与符号条件混用易混淆，学习成本上升。

## Alternatives
[alternatives]: #alternatives

- 沿用现有 `#If` 编译符号手工维护，不引入语义谓词——简单但无法可靠表达"API 是否存在"。
- 在项目文件（`<Choose>`/`<When>`）做条件编译，把判断留在 MSBuild——语义仍未知。
- 让源码生成器自行用 Roslyn API 探测类型/成员并生成条件化代码——能力相同但生成器作者负担重。

## Unresolved questions
[unresolved]: #unresolved-questions

- `METHOD_EXISTS`/`TYPE_EXISTS`/`MEMBER_EXISTS` 的参数形态（是否支持对象表达式 `obj.Member`、类型名、字符串形式的成员名）。
- 语义检查与二进制重编译/增量编译的性能模型（是否允许每次编辑都全量绑定）。
- 与现有 `#If` 符号条件如何组合（`##If ... AndAlso 符号`）。
- 原文最后一个示例以 `#End If`（单 `#`）结束，疑似笔误，需确认规范是否统一为 `##End If`。
