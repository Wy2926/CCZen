---
title: CCZen 技术架构与工程规范
description: .NET 技术选型、进程模型、性能与内存预算、兼容矩阵、测试策略与里程碑。
ms.topic: architecture
ms.date: 2026-07-07
status: Draft v0.2
applies-to: Windows 10 版本 1809 及更高版本
---

# 技术架构与工程规范

## 技术选型（.NET 全栈）

| 层 | 选型 | 依据 |
|----|------|------|
| 运行时 | [.NET 8 (LTS)](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8/overview)，C# 12 | LTS 支持至 2026-11；后续升级 .NET 10 LTS |
| UI | [WinUI 3（Windows App SDK）](https://learn.microsoft.com/windows/apps/winui/winui3/) + CommunityToolkit.Mvvm | 微软当前主推的桌面 UI 栈；Win10 1809+ 支持 |
| Win32 互操作 | [CsWin32 源生成器](https://github.com/microsoft/CsWin32)（微软官方） | 强类型 P/Invoke，覆盖本 spec 全部 API |
| 引擎 | CCZen.Engine 类库 + 通用宿主（`Microsoft.Extensions.Hosting`） | 标准 .NET 模式 |
| IPC | `System.IO.Pipes` 命名管道 + [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc)（微软官方 JSON-RPC 库） | 成熟先例（VS 内部使用） |
| 序列化 | `System.Text.Json`（索引缓存用二进制自定义格式 + `XxHash64` 校验，[System.IO.Hashing](https://learn.microsoft.com/dotnet/api/system.io.hashing)） | BCL |
| 规则校验 | JSON Schema（JsonSchema.Net）+ `ECDsa` 签名 | 见 04 |
| 打包 | MSIX（商店/侧载）+ [MSI/Setup 备选]；自包含发布（self-contained，免装运行时） | [.NET 部署文档](https://learn.microsoft.com/dotnet/core/deploying/) |
| 基准/测试 | xUnit + FsCheck + [BenchmarkDotNet](https://benchmarkdotnet.org/) + WinAppDriver/FlaUI（UI 自动化） | 成熟生态 |

> 说明：引擎热点路径（MFT 记录解析、索引结构）使用 `Span<T>`/`Memory<T>`、`ArrayPool<T>`、struct-of-arrays 与字符串池；这是 .NET 8 公开支持的高性能模式（`System.IO.Enumeration` 即为官方先例），可满足性能预算，无需 C++/CLI。

## 进程模型

```
CCZen.App    (WinUI 3, 标准权限)
   │ NamedPipe + StreamJsonRpc
CCZen.Engine (标准权限；可选托盘常驻做 USN 保鲜)
   │ NamedPipe（PipeSecurity ACL + 客户端签名校验，见 SAFE-FR-041）
CCZen.Helper (按需 UAC 提权：卷句柄读取、系统区批次执行)
```

- 无常驻模式：Engine 随 App 启停，靠索引缓存 + USN 追赶实现秒开（SCAN-FR-032）
- 常驻模式（用户可选）：托盘进程持续消费 USN 日志

## 性能与资源预算

| ID | 项 | 预算 | 达成手段 |
|----|-----|------|----------|
| ARCH-NFR-001 | 索引内存（400 万文件） | < 400 MB | struct 数组 + 字符串池；Server GC 关闭、`GCSettings.LatencyMode` 调优 |
| ARCH-NFR-002 | 空闲常驻内存（托盘） | < 120 MB | 索引可压缩/换出到缓存文件 |
| ARCH-NFR-003 | UI 首屏（热启动） | < 1.5 s | 缓存加载 + 增量渲染 |
| ARCH-NFR-004 | 冷全量扫描前台影响 | 无感 | 低优先级线程/IO（SCAN-NFR-003） |
| ARCH-NFR-005 | 安装体积 | < 80 MB（自包含）/ < 15 MB（依赖运行时） | Trimming（引擎部分）按 .NET 官方支持范围使用 |

## 兼容矩阵

- Windows 10 1809+ x64、Windows 11 x64/ARM64（.NET 8 与 Windows App SDK 均官方支持 ARM64）
- 文件系统：NTFS（快速路径）；ReFS/exFAT/FAT32/Dev Drive（回退路径，SCAN-FR-040）
- 多用户：默认清当前用户 + 系统区；管理员可选"所有用户"
- 长路径、非 ASCII 用户名、OneDrive Known Folder Move、企业漫游配置文件
- 与存储感知/第三方清理器共存：不注册冲突钩子，仅提示

## 工程流程（ECC 准则落地）

- **Spec 先行**：`docs/specs/` 为唯一需求真源；实现 PR 必须引用需求 ID；新增需求必须同步登记 06 追溯表
- **TDD**（tdd-workflow skill）：先失败测试后实现；覆盖率 ≥ 80%（coverlet）
- 测试金字塔：
  - 单元：MFT 记录解析（二进制夹具）、评分器、规则 Schema/DSL
  - 集成：VHDX 虚拟卷夹具（`New-VHD` 可脚本化）端到端 扫描→推荐→隔离→还原
  - UI E2E：FlaUI/WinAppDriver（windows-desktop-e2e skill）
  - 性能门禁：BenchmarkDotNet 基准回归（SCAN-NFR-001/002）
- **安全审查**（security-review skill）：删除路径、提权 IPC、验签相关 PR 强制审查
- 提交规范：`feat|fix|refactor|docs|test|chore|perf|ci: <desc>`

## 里程碑

| 里程碑 | 内容 | 退出标准 |
|--------|------|----------|
| M0 POC | USN 枚举 + `FSCTL_GET_NTFS_FILE_RECORD` 尺寸 + Top-N 控制台工具 | 100 万文件 < 10 s；与 WizTree 结果抽检一致 |
| M1 扫描产品化 | USN 增量、索引缓存、回退路径、查询服务 | 01 全部验收项 |
| M2 规则引擎 | 环境发现/证据/评分管线 + T0 系统类别 + 隔离区/撤销 | 02/04 golden 测试全绿 |
| M3 适配层 | 首发 Adapter 集 + 规则包签名分发 | 03 夹具矩阵全绿 |
| M4 UI/发布 | treemap 大文件浏览器、一键清理、撤销中心、MSIX 打包 | E2E 全绿 + 内测误删 0 |

## 开放问题（需 ADR 决议）

- D7：托盘常驻默认开关
- D8：重复文件全量哈希调度（仅手动 vs 空闲自动）
- D9：企业静默模式（v2）
- D10：索引缓存格式（自定义二进制 vs MemoryPack 等库）——M1 前 spike 决定

## 另请参阅

- [.NET 应用发布与部署](https://learn.microsoft.com/dotnet/core/deploying/)
- [Windows App SDK 系统要求](https://learn.microsoft.com/windows/apps/windows-app-sdk/system-requirements)
- 06 追溯表条目 T-01 ~ T-08
