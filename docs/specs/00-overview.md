---
title: CCZen 产品与架构概述
description: 面向 Windows 10 版本 1809 及更高版本的磁盘清理软件 CCZen 的产品定位、体系结构与需求索引。
ms.topic: overview
ms.date: 2026-07-07
status: Draft v0.2
applies-to: Windows 10 版本 1809 (build 17763) 及更高版本、Windows 11
---

# CCZen 产品与架构概述

CCZen 是一款基于 **.NET** 的 Windows 磁盘清理软件。本文档集是项目的唯一需求真源（single source of truth）。

## 本文档集的约定

- 需求关键字 **必须（MUST）**、**应（SHOULD）**、**可以（MAY）** 按 [RFC 2119](https://www.rfc-editor.org/rfc/rfc2119) 解释。
- 每条功能需求都有唯一 ID（如 `SCAN-FR-001`），并且**必须在 [06-feasibility-matrix.md](06-feasibility-matrix.md) 中登记其实现依据**——公开文档记载的 Windows API、.NET API 或经过验证的成熟软件先例。
- **禁止空想需求**：任何无法在追溯表中给出实现依据的需求，不得进入规范；发现后按缺陷处理。
- 文档结构与措辞遵循 Microsoft Learn 文档风格（元数据块、适用范围、需求表、"另请参阅"）。

## 产品定位

| 目标 | 说明 |
|------|------|
| 秒级定位空间占用 | 与 Everything/WizTree 同源的 NTFS 索引技术，秒级列出大文件与大文件夹 |
| 本地智能推荐 | 规则引擎 + 多信号启发式评分产生"推荐清理"；**不依赖任何 LLM，完全离线可用** |
| 通用而非写死 | 清理目标通过"环境发现 → 证据 → 评分"推导；主流应用以声明式适配清单增强 |
| 安全第一 | 分级风险模型、隔离区可撤销删除、保护清单；宁可少清不可误删 |

## 体系结构

```
CCZen.App        WinUI 3 (Windows App SDK) + .NET 8，标准用户权限
      │  命名管道 (System.IO.Pipes) + JSON-RPC
CCZen.Engine     .NET 8 类库/宿主进程：扫描引擎、规则引擎、执行引擎、索引缓存
      │  命名管道（调用方签名校验）
CCZen.Helper     按需 UAC 提权的辅助进程：卷句柄读取、系统区清理执行
```

组件与对应规范：

| 规范 | 内容 |
|------|------|
| [01-scan-engine.md](01-scan-engine.md) | 扫描引擎：USN/MFT 全卷索引、目录大小聚合、增量维护、回退路径 |
| [02-rules-engine.md](02-rules-engine.md) | 规则引擎：环境发现、候选生成、证据评分、风险分级、规则包格式 |
| [03-app-adapters.md](03-app-adapters.md) | 应用适配层：主流应用的声明式清理清单 |
| [04-safety-model.md](04-safety-model.md) | 安全模型：保护清单、隔离区、撤销、占用检测、签名与权限 |
| [05-architecture.md](05-architecture.md) | .NET 技术架构、进程/权限模型、性能预算、测试与里程碑 |
| [06-feasibility-matrix.md](06-feasibility-matrix.md) | 需求 → API/先例 可行性追溯表（强制维护） |

## 关键设计决策

| # | 决策 | 实现依据（详见 06） |
|---|------|---------------------|
| D1 | 通过 USN 变更日志枚举（`FSCTL_ENUM_USN_DATA`）建立 NTFS 全卷索引，而非目录树遍历 | 公开 Win32 API；Everything 官方 FAQ 记载同款技术 |
| D2 | USN 日志增量维护（`FSCTL_READ_USN_JOURNAL`）+ 索引落盘缓存 | 公开 Win32 API |
| D3 | 清理智能 = 声明式规则包 + 启发式评分，零 LLM | 纯本地计算，System.Text.Json 解析 |
| D4 | 通用启发式为主、适配清单为辅 | 规则数据化，无代码执行 |
| D5 | 删除默认可撤销：回收站（`IFileOperation`）或同卷改名隔离区 | 公开 Shell API / `File.Move` |
| D6 | 全栈 .NET 8（C#）：引擎类库 + WinUI 3 UI + 提权 Helper；Win32 互操作使用 CsWin32 源生成器 | 微软官方支持的技术栈 |

## 非目标（v1）

- 注册表"优化"、系统加速类功能
- 驱动级文件粉碎、碎片整理
- macOS/Linux
- 云端账户体系（规则包更新仅为签名静态文件下载）

## 验收指标

| ID | 指标 | 目标 |
|----|------|------|
| OVR-NFR-001 | 全卷索引（100 万文件，NVMe，冷启动，管理员） | < 10 s |
| OVR-NFR-002 | 热启动（缓存加载 + USN 追赶） | < 1 s |
| OVR-NFR-003 | Top-N 大文件/大文件夹查询 | < 100 ms |
| OVR-NFR-004 | 索引内存占用（400 万文件） | < 400 MB（.NET 托管堆实测预算，见 05） |
| OVR-NFR-005 | 非用户确认项误删 | 0 容忍；隔离区兜底 |

## 另请参阅

- [Everything 官方 FAQ — 索引原理](https://www.voidtools.com/faq/)（"Everything indexes the NTFS Master File Table and USN Journal"）
- [变更日志（Change Journals）— Win32](https://learn.microsoft.com/windows/win32/fileio/change-journals)
- [.NET 8 (LTS)](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8/overview)
- [WinUI 3 / Windows App SDK](https://learn.microsoft.com/windows/apps/winui/winui3/)
