---
title: CCZen 安全模型规范
description: 保护清单、事务化执行、隔离区与撤销、占用检测、规则包签名与最小权限模型的需求定义。
ms.topic: reference
ms.date: 2026-07-07
status: Draft v0.2
applies-to: Windows 10 版本 1809 及更高版本
---

# 安全模型规范

清理软件的生命线是"从不误删"。本规范定义删除路径上的全部防线。所有机制均基于公开 API 与 .NET BCL（追溯表 06 条目 S-xx）。

```
保护清单(一票否决) → 风险分级(02) → 用户确认 → 占用/前置检查
   → 事务化执行 → 隔离区/回收站 → 撤销中心 → 审计日志
```

## 保护清单（Protected Paths）

| ID | 需求 | 级别 |
|----|------|------|
| SAFE-FR-001 | 保护清单**必须**硬编码于引擎，规则包不可覆盖或缩减 | MUST |
| SAFE-FR-002 | 保护范围**必须**包含：`%WINDIR%` 核心（白名单化缓存子目录除外）、`Program Files*` 可执行本体、EFI/恢复分区、用户资产根（Documents/Pictures/Videos/Desktop 及其 Known Folder 重定向真实位置）、浏览器书签/密码/Cookies 库、邮件存储（PST/OST）、卷根、`System Volume Information` | MUST |
| SAFE-FR-003 | 路径**必须**先规范化（`Path.GetFullPath` + [`GetFinalPathNameByHandle`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew) 解析符号链接真实目标）再做保护校验，防 `..`、符号链接、8.3 短名绕过 | MUST |
| SAFE-FR-004 | 任何带 `FILE_ATTRIBUTE_REPARSE_POINT` 的链接**必须**只删除链接本体语义可控的场景才允许，且绝不穿越删除目标内容 | MUST |
| SAFE-FR-005 | 清空回收站**必须**走 `SHEmptyRecycleBin`，不直接操作 `$Recycle.Bin` 结构 | MUST |

## 事务化执行引擎

| ID | 需求 | 级别 |
|----|------|------|
| SAFE-FR-010 | 每次清理为一个**批次**：先生成不可变执行计划（文件列表、动作、预估空间）；用户确认的是计划快照 | MUST |
| SAFE-FR-011 | 执行时逐项校验指纹（路径 + 大小 + 修改时间）匹配才执行，防 TOCTOU 替换 | MUST |
| SAFE-FR-012 | 批次日志**必须**先写后删（WAL：`FileStream` + `Flush(true)`）；崩溃/断电后可恢复或回滚，无半删状态 | MUST |
| SAFE-FR-013 | 需要停服务的项（如 `wuauserv`）**必须**用 [`ServiceController`](https://learn.microsoft.com/dotnet/api/system.serviceprocess.servicecontroller) 停→删→恢复；失败则恢复服务并中止 | MUST |

## 删除通道

| 通道 | 实现 | 用途 |
|------|------|------|
| 回收站 | [`IFileOperation`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation) + `FOFX_RECYCLEONDELETE`（COM 互操作） | 小体量、用户可见性高的项 |
| **隔离区（默认）** | 同卷 `File.Move`/`MoveFileEx`（同卷改名为 O(1) 元数据操作）至 `<卷>\CCZen.Quarantine\<batchId>\`，保留原路径映射清单 | 绝大多数项 |
| 直接删除 | `File.Delete`/`RemoveDirectory` | 仅 T0 大体量项且用户显式允许 |
| 官方通道委托 | powercfg、DISM、`Delete-DeliveryOptimizationCache`、cleanmgr、`Optimize-VHD` 等（见 02/03） | 系统级项，绝不自行动文件 |

| ID | 需求 | 级别 |
|----|------|------|
| SAFE-FR-020 | 隔离区**必须**与源文件同卷（跨卷项自动改走回收站通道） | MUST |
| SAFE-FR-021 | 删除前**必须**用 [Restart Manager](https://learn.microsoft.com/windows/win32/rstmgr/restart-manager-portal)（`RmStartSession`/`RmRegisterResources`/`RmGetList`）查询占用进程；被占用项默认跳过并说明 | MUST |
| SAFE-FR-022 | **不得**强制解锁或结束进程；"退出并清理"只允许对用户显式选择的应用发送优雅关闭（`Process.CloseMainWindow`） | MUST |

## 隔离区与撤销

| ID | 需求 | 级别 |
|----|------|------|
| SAFE-FR-025 | 隔离区默认保留 7 天（可调 1–30 天），到期后台真删；空间紧张时提示但仍需确认 | MUST |
| SAFE-FR-026 | 撤销中心**必须**支持按批次一键还原（同卷改名回原路径；冲突时还原为 `.restored` 副本并提示） | MUST |
| SAFE-FR-027 | 隔离区大小**必须**全程计入 UI"可再释放空间"展示 | MUST |
| SAFE-FR-028 | 隔离区目录 ACL **必须**限制为 Administrators + 当前用户（`FileSystemSecurity`/[`SetNamedSecurityInfo`](https://learn.microsoft.com/windows/win32/api/aclapi/nf-aclapi-setnamedsecurityinfow)） | MUST |

## 规则包与更新安全

| ID | 需求 | 级别 |
|----|------|------|
| SAFE-FR-030 | 规则包/Adapter 更新文件**必须**带 ECDSA P-256 签名（[`System.Security.Cryptography.ECDsa`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.ecdsa)，.NET 内置，无第三方依赖）；公钥固化在主程序内；验签失败回退内置基线包 | MUST |
| SAFE-FR-031 | 主程序与 Helper **必须** Authenticode 签名；自动更新走 HTTPS（`HttpClient` + 证书校验）+ 包签名双校验 | MUST |
| SAFE-FR-032 | 规则包**必须**无代码执行能力：动作只能引用引擎内置动作枚举 | MUST |

## 权限模型

| ID | 需求 | 级别 |
|----|------|------|
| SAFE-FR-040 | UI/Engine 进程始终标准权限；需要卷句柄或系统区操作时按需以 [UAC 提权（`ProcessStartInfo.Verb = "runas"`）](https://learn.microsoft.com/windows/win32/secauthz/user-account-control) 启动 CCZen.Helper | MUST |
| SAFE-FR-041 | Helper IPC（`NamedPipeServerStream`）**必须**校验客户端：管道 ACL（[`PipeSecurity`](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipesecurity)）+ 客户端进程可执行文件 Authenticode 签名校验 | MUST |
| SAFE-FR-042 | Helper 只接受白名单动作（枚举卷、执行已签名批次计划），**不得**提供任意路径删除接口 | MUST |

## 审计与可观测

| ID | 需求 | 级别 |
|----|------|------|
| SAFE-FR-050 | 本地审计日志（JSON Lines，滚动上限）：每批次计划、实际执行、跳过原因、还原记录 | MUST |
| SAFE-FR-051 | 默认零联网；更新检查可关闭 | MUST |

## 测试要求

- 保护清单穿透测试：junction/符号链接指向 `%WINDIR%` 的 cache 命名目录 → 必须拒绝
- 故障注入：批次执行中 kill 进程/模拟断电 → 重启后无半删状态、可续或回滚
- 还原冲突、跨卷隔离区禁用断言
- 权限探针：未签名客户端调用 Helper → 拒绝

## 另请参阅

- [Restart Manager](https://learn.microsoft.com/windows/win32/rstmgr/about-restart-manager)
- [IFileOperation](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation)
- 06 追溯表条目 S-01 ~ S-12
