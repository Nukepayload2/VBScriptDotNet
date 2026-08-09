# 运行时宿主选择 / Runtime Host Selection

* [x] Proposed
* [x] Implementation: Complete
* [x] Specification: Complete

## Summary
[summary]: #summary

记录 VBScript.NET **1.2 版**（微软商店已发布）已实现的运行时宿主选择：文件关联运行时按脚本头部 `' Attribute TargetFramework = "net48"` 注释，在 **net48 宿主**与 **.NET 宿主**之间选择执行环境。

## Motivation
[motivation]: #motivation

1.2 同时支持 .NET（net6.0 → net8.0）与 .NET Framework 4.8，需要按脚本目标框架选择宿主。文件关联（双击 .vbx / `vbi.exe` 启动）按脚本头部注释决定使用哪套运行时。

## Detailed design
[design]: #detailed-design

1.2 已发布能力（1.2 版本归档，事实自包含）：

- 脚本头部写 `' Attribute TargetFramework = "net48"` 时，走 **net48 宿主**（商店版中为 `vbifw`，net48 版 `vbi`）。
- 未标注时默认走 **.NET 宿主**。
- 文件关联在启动时读取脚本头部注释，据此选择对应宿主。

## Drawbacks
[drawbacks]: #drawbacks

- 宿主选择依赖头部注释约定，遗漏或拼写错误会落到默认宿主，可能引发运行时行为差异。

## Alternatives
[alternatives]: #alternatives

- 仅单一宿主（1.2 未采用）；双宿主 + 头部注释选择是兼容 net48 与 .NET 的取舍。

## Unresolved questions
[unresolved]: #unresolved-questions

- 无——本文件为 1.2 已发布能力记录（done）。
