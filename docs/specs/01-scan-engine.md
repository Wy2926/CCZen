---
title: CCZen 扫描引擎规范
description: 基于 NTFS USN/MFT 的秒级全卷索引、目录大小聚合与增量维护的功能与非功能需求。
ms.topic: reference
ms.date: 2026-07-07
status: Draft v0.2
applies-to: Windows 10 版本 1809 及更高版本；NTFS 卷（快速路径）、FAT32/exFAT/ReFS（回退路径）
---

# 扫描引擎规范

扫描引擎为规则引擎与 UI 提供全卷文件索引与尺寸聚合查询。目标：达到 Everything/WizTree 级别的建索引与查询速度。所有 API 均为微软公开文档记载的 API；.NET 侧通过 [CsWin32](https://github.com/microsoft/CsWin32) 源生成器进行 P/Invoke。

## 背景：为什么不遍历目录树

递归 `FindFirstFile`/`Directory.EnumerateFiles` 对每个目录产生系统调用与随机 I/O，百万文件量级耗时数分钟。Everything 官方 FAQ 明确记载其速度来自**读取 NTFS 主文件表（MFT）与 USN 变更日志**而非遍历目录（见 06 追溯表 F-01）。WizTree 采用同一技术实现秒级空间分析。CCZen 采用同一路线。

## 功能需求 — NTFS 快速路径

| ID | 需求 | 级别 |
|----|------|------|
| SCAN-FR-001 | 引擎**必须**通过 `DeviceIoControl` + [`FSCTL_ENUM_USN_DATA`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_enum_usn_data) 按 FRN 顺序批量枚举 NTFS 卷全部文件/目录记录（`USN_RECORD_V2/V3`：FRN、父 FRN、文件名、属性），单次调用使用 ≥ 1 MB 输出缓冲区 | MUST |
| SCAN-FR-002 | 卷句柄**必须**以 `CreateFileW("\\\\.\\C:", FILE_READ_DATA \| FILE_READ_ATTRIBUTES, ...)` 打开；该操作需要管理员权限，由 CCZen.Helper 进程执行（见 04 权限模型） | MUST |
| SCAN-FR-003 | 文件尺寸**必须**通过 [`FSCTL_GET_NTFS_FILE_RECORD`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_get_ntfs_file_record) 批量读取 MFT 文件记录，解析 `$STANDARD_INFORMATION`/`$FILE_NAME`/`$DATA` 属性获得逻辑大小与分配大小（该 FSCTL 顺序读取时等效于顺序扫描 $MFT，WizTree 同类实现先例见 06 F-03） | MUST |
| SCAN-FR-004 | 尺寸解析失败的记录**必须**回退到 [`GetFileInformationByHandleEx(FileIdBothDirectoryInfo)`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex) 按目录批量补齐 | MUST |
| SCAN-FR-005 | 多 NTFS 卷**应**并行建索引（每卷一个工作线程，`Task`/`Channel` 调度） | SHOULD |

### 尺寸统计口径

| ID | 需求 | 级别 |
|----|------|------|
| SCAN-FR-010 | 硬链接（同一 FRN 多个 `$FILE_NAME`）占用**必须**按 FRN 去重只计一次，UI 标注链接数（链接数可由 [`GetFileInformationByHandle`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandle) 的 `nNumberOfLinks` 验证） | MUST |
| SCAN-FR-011 | 稀疏/压缩文件**必须**以分配大小（真实占盘）为默认统计口径，逻辑大小并列展示；单文件校验可用 [`GetCompressedFileSizeW`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) | MUST |
| SCAN-FR-012 | 重解析点（junction/symlink，`FILE_ATTRIBUTE_REPARSE_POINT`）**必须**不跟随、不重复计数 | MUST |
| SCAN-FR-013 | 云占位文件（`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`，OneDrive 按需文件）占用**必须**按本地分配大小统计 | MUST |
| SCAN-FR-014 | NTFS 备用数据流（ADS）v1 计入所属文件分配大小，**可以**不单独枚举 | MAY |

### 索引与聚合

| ID | 需求 | 级别 |
|----|------|------|
| SCAN-FR-020 | 索引**必须**采用值类型结构数组（struct-of-arrays）+ 字符串池存储；禁止每文件一个托管对象（GC 压力控制，见 05 内存预算） | MUST |
| SCAN-FR-021 | 全量枚举完成后**必须**做一次自底向上聚合：每目录的累计逻辑/分配大小、文件数、最新修改时间 | MUST |
| SCAN-FR-022 | **必须**提供查询：Top-N 大文件、Top-N 大文件夹、目录子树钻取（treemap 数据源）、glob 路径匹配（供规则引擎）；全部在内存索引上执行 | MUST |
| SCAN-FR-023 | 查询与增量更新**必须**并发安全（快照或 `ReaderWriterLockSlim`） | MUST |

### USN 增量维护与缓存

| ID | 需求 | 级别 |
|----|------|------|
| SCAN-FR-030 | 全量枚举结束时**必须**记录 [`FSCTL_QUERY_USN_JOURNAL`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_query_usn_journal) 返回的 `UsnJournalID` 与 `NextUsn` 水位 | MUST |
| SCAN-FR-031 | 后台**必须**通过 [`FSCTL_READ_USN_JOURNAL`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_read_usn_journal) 消费创建/删除/改名/扩展变更记录，实时更新索引并沿父链传播尺寸差量 | MUST |
| SCAN-FR-032 | 索引**必须**支持落盘缓存（含 USN 水位与校验和）；启动时加载缓存 → 读日志追赶 → 秒级就绪 | MUST |
| SCAN-FR-033 | 日志被截断（`ERROR_JOURNAL_ENTRY_DELETED`）、JournalID 变化或缓存校验失败时**必须**自动全量重建 | MUST |
| SCAN-FR-034 | 卷未启用 USN 日志时**必须**能通过 [`FSCTL_CREATE_USN_JOURNAL`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_create_usn_journal) 创建（需用户知情同意，日志占用少量磁盘空间） | MUST |

## 功能需求 — 回退路径（非 NTFS / 无管理员）

| ID | 需求 | 级别 |
|----|------|------|
| SCAN-FR-040 | 非 NTFS 卷或无提权时**必须**回退为并行目录遍历：.NET [`FileSystemEnumerable<T>`](https://learn.microsoft.com/dotnet/api/system.io.enumeration.filesystemenumerable-1)（底层批量 `NtQueryDirectoryFile`，零额外分配）+ 每卷工作队列 | MUST |
| SCAN-FR-041 | 卷类型**必须**通过 [`GetVolumeInformationW`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getvolumeinformationw) 判定；HDD（`DeviceIoControl(IOCTL_STORAGE_QUERY_PROPERTY)` 的 SeekPenalty 判定）遍历并发度**应**降为 1–2 | MUST/SHOULD |
| SCAN-FR-042 | 回退路径**必须**使用与快速路径相同的索引结构与聚合逻辑（仅速度降级） | MUST |
| SCAN-FR-043 | 长路径**必须**全程使用 `\\?\` 前缀方式处理（.NET 4.6.2+/Core 原生支持长路径） | MUST |
| SCAN-FR-044 | 网络驱动器**必须**默认不扫描，需用户显式勾选 | MUST |

## 非功能需求

| ID | 需求 |
|----|------|
| SCAN-NFR-001 | 冷全量：100 万文件 NVMe < 10 s；500 万文件 < 45 s（管理员 + NTFS） |
| SCAN-NFR-002 | 热启动 < 1 s；Top-N 查询 < 100 ms |
| SCAN-NFR-003 | 扫描线程必须以低优先级运行：`Thread.Priority = Lowest` + [`SetThreadIOPriorityHint`（`SetThreadInformation` + `MEMORY_PRIORITY_INFORMATION`/IO 优先级）](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadinformation)，前台无感 |
| SCAN-NFR-004 | 扫描过程零写入被扫卷；缓存写入 `%LOCALAPPDATA%\CCZen` |
| SCAN-NFR-005 | 卷热插拔、卷锁定、缓存损坏必须优雅降级为重建，不崩溃 |

## 测试要求

- 用 VHDX 虚拟磁盘构造测试卷夹具（`New-VHD`/diskpart 脚本，CI 可自动挂载）：硬链接、稀疏、压缩、junction 环、长路径、非 ASCII/emoji 文件名
- MFT 解析结果与 `FileInfo`/`GetFileInformationByHandle` 抽样比对一致率 ≥ 99.99%
- USN 增量一致性：随机文件操作风暴后索引与文件系统 diff 为零
- BenchmarkDotNet 基准固化进 CI 作为性能回归门禁

## 另请参阅

- [Change Journals（变更日志）概述](https://learn.microsoft.com/windows/win32/fileio/change-journals)
- [Walking a Buffer of Change Journal Records（官方示例）](https://learn.microsoft.com/windows/win32/fileio/walking-a-buffer-of-change-journal-records)
- [Master File Table（MFT）](https://learn.microsoft.com/windows/win32/fileio/master-file-table)
- 06 追溯表条目 F-01 ~ F-12
