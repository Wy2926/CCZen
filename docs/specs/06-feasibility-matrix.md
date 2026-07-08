---
title: CCZen 可行性追溯表
description: 每条关键需求到公开 API、官方文档或成熟软件先例的强制映射。无法登记依据的需求不得进入规范。
ms.topic: reference
ms.date: 2026-07-08
status: Draft v0.3
applies-to: 全部规范文档（00–05）
---

# 可行性追溯表（Feasibility Traceability Matrix）

本表是 00 中"禁止空想需求"约定的落地机制。**任何进入规范的需求，必须能在本表找到实现依据**；PR 新增需求时必须同步登记，CI 文档检查（需求 ID 引用完整性）作为门禁。

依据类型：`API`（微软公开 API 文档）、`OFFICIAL`（微软/厂商官方流程文档）、`PRECEDENT`（成熟软件已验证的公开做法）、`BCL`（.NET 基础类库）。

## F — 扫描引擎（对应 SCAN-FR/NFR）

| # | 需求 | 依据 | 参考 |
|---|------|------|------|
| F-01 | 不遍历目录树、秒级建索引整体路线 | PRECEDENT | [Everything FAQ](https://www.voidtools.com/faq/)：官方说明其索引 NTFS MFT 与 USN 日志；WizTree 同技术 |
| F-02 | 全卷枚举（SCAN-FR-001） | API | [`FSCTL_ENUM_USN_DATA`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_enum_usn_data)、[`USN_RECORD_V2`](https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-usn_record_v2) |
| F-03 | MFT 记录读取取尺寸（SCAN-FR-003） | API + PRECEDENT | [`FSCTL_GET_NTFS_FILE_RECORD`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_get_ntfs_file_record)、[`NTFS_FILE_RECORD_OUTPUT_BUFFER`](https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-ntfs_file_record_output_buffer)；MFT 属性结构见 [Master File Table](https://learn.microsoft.com/windows/win32/fileio/master-file-table)；开源先例：NtfsReader（C#）、everything-like 索引器多个 C# 实现 |
| F-04 | 目录批量补齐（SCAN-FR-004） | API | [`GetFileInformationByHandleEx(FileIdBothDirectoryInfo)`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex) |
| F-05 | 卷句柄打开需管理员（SCAN-FR-002） | API | [`CreateFileW` 物理磁盘与卷说明](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew#physical-disks-and-volumes) |
| F-06 | USN 水位/读取/创建（SCAN-FR-030/031/034） | API | [`FSCTL_QUERY_USN_JOURNAL`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_query_usn_journal)、[`FSCTL_READ_USN_JOURNAL`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_read_usn_journal)、[`FSCTL_CREATE_USN_JOURNAL`](https://learn.microsoft.com/windows/win32/api/winioctl/ni-winioctl-fsctl_create_usn_journal)；官方示例 [Walking a Buffer of Change Journal Records](https://learn.microsoft.com/windows/win32/fileio/walking-a-buffer-of-change-journal-records) |
| F-07 | 硬链接/压缩/稀疏口径（SCAN-FR-010/011） | API | [`GetFileInformationByHandle`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandle)（nNumberOfLinks）、[`GetCompressedFileSizeW`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew) |
| F-08 | 云占位文件属性（SCAN-FR-013） | API | [`FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`](https://learn.microsoft.com/windows/win32/fileio/file-attribute-constants) |
| F-09 | 高性能回退遍历（SCAN-FR-040） | BCL | [`System.IO.Enumeration.FileSystemEnumerable<T>`](https://learn.microsoft.com/dotnet/api/system.io.enumeration.filesystemenumerable-1)（.NET 官方低分配枚举 API） |
| F-10 | SSD/HDD 判定（SCAN-FR-041） | API | [`IOCTL_STORAGE_QUERY_PROPERTY` + `DEVICE_SEEK_PENALTY_DESCRIPTOR`](https://learn.microsoft.com/windows/win32/api/winioctl/ns-winioctl-device_seek_penalty_descriptor) |
| F-11 | 低优先级 I/O（SCAN-NFR-003） | API | [`SetThreadInformation(ThreadMemoryPriority/ThreadIoPriority)`](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-setthreadinformation)、[后台任务优先级概述](https://learn.microsoft.com/windows/win32/procthread/scheduling-priorities) |
| F-12 | 长路径（SCAN-FR-043） | OFFICIAL | [Maximum Path Length Limitation](https://learn.microsoft.com/windows/win32/fileio/maximum-file-path-limitation)（.NET Core 默认支持长路径） |
| F-13 | MFT `$STANDARD_INFORMATION` 修改时间（SCAN-FR-026） | API | [Master File Table](https://learn.microsoft.com/windows/win32/fileio/master-file-table) — `$STANDARD_INFORMATION` 属性 |
| F-14 | 索引内存 glob/前缀/子树查询（SCAN-FR-022/028） | PRECEDENT + BCL | Everything 类索引器 in-memory filter 先例；BCL `MemoryExtensions` / struct-of-arrays 遍历 |

## R — 规则引擎（对应 RULE-FR）

| # | 需求 | 依据 | 参考 |
|---|------|------|------|
| R-01 | Known Folders（RULE-FR-001） | API | [`SHGetKnownFolderPath`](https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shgetknownfolderpath)、[KNOWNFOLDERID](https://learn.microsoft.com/windows/win32/shell/knownfolderid) |
| R-02 | 已安装应用清单（RULE-FR-002） | OFFICIAL | [Uninstall Registry Key](https://learn.microsoft.com/windows/win32/msi/uninstall-registry-key) |
| R-03 | MSIX 应用枚举（RULE-FR-003） | API | [`PackageManager`](https://learn.microsoft.com/uwp/api/windows.management.deployment.packagemanager) |
| R-04 | 包管理器缓存定位（RULE-FR-004） | OFFICIAL | [npm config](https://docs.npmjs.com/cli/v10/using-npm/config#cache)、[pip cache](https://pip.pypa.io/en/stable/cli/pip_cache/)、[Gradle 目录](https://docs.gradle.org/current/userguide/directory_layout.html)、[NuGet 缓存](https://learn.microsoft.com/nuget/consume-packages/managing-the-global-packages-and-cache-folders) |
| R-05 | 回收站统计/清空（RULE-FR-011） | API | [`SHQueryRecycleBin`](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shqueryrecyclebin)、[`SHEmptyRecycleBin`](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shemptyrecyclebin) |
| R-06 | WU 缓存清理步骤（RULE-FR-012） | OFFICIAL | 微软 Windows Update 排障文档公开步骤（停 wuauserv → 清空 SoftwareDistribution → 重启服务），如 [Windows Update 重置指引](https://learn.microsoft.com/troubleshoot/windows-client/installing-updates-features-roles/additional-resources-for-windows-update) |
| R-07 | 传递优化缓存（RULE-FR-013） | OFFICIAL | [Delivery Optimization PowerShell（`Delete-DeliveryOptimizationCache`）](https://learn.microsoft.com/windows/deployment/do/waas-delivery-optimization-setup#monitor-delivery-optimization) |
| R-08 | Windows.old / 磁盘清理通道（RULE-FR-014） | OFFICIAL | [cleanmgr 命令与 /SAGESET 自动化](https://learn.microsoft.com/windows-server/administration/windows-commands/cleanmgr) |
| R-09 | 休眠文件（RULE-FR-017） | OFFICIAL | [powercfg /hibernate](https://learn.microsoft.com/windows-hardware/design/device-experiences/powercfg-command-line-options#option_hibernate) |
| R-10 | WinSxS（RULE-FR-018） | OFFICIAL | [Clean Up the WinSxS Folder（DISM StartComponentCleanup）](https://learn.microsoft.com/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder) |
| R-11 | LastAccess 可用性判定 | OFFICIAL | [fsutil behavior disablelastaccess](https://learn.microsoft.com/windows-server/administration/windows-commands/fsutil-behavior) |
| R-12 | 占用探测 | API | [Restart Manager `RmGetList`](https://learn.microsoft.com/windows/win32/api/restartmgr/nf-restartmgr-rmgetlist) |
| R-13 | 哈希查重（RULE-FR-024） | BCL | [`System.IO.Hashing.XxHash64`](https://learn.microsoft.com/dotnet/api/system.io.hashing.xxhash64)、`SHA256` |
| R-14 | T0 类别与系统行为对齐 | PRECEDENT | [Storage Sense 清理范围](https://support.microsoft.com/windows/manage-drive-space-with-storage-sense-654f6ada-7bfc-45e5-966b-e24aded96ad5)、cleanmgr 内置处理程序 |
| R-15 | 索引驱动候选生成（RULE-FR-026/027） | 内部架构 | 依赖 F-01/F-13/F-14；[方案 B 实施计划](../plans/index-merge-scheme-b.md) |

## A — 应用适配（对应 ADPT-FR；每个 Adapter 上线前逐项补登）

| # | 需求 | 依据 | 参考 |
|---|------|------|------|
| A-01 | Chromium 缓存目录语义 | PRECEDENT | Chromium 开源代码 `disk_cache`/profile 目录结构；Chrome 官方"清除缓存"功能语义一致 |
| A-02 | 微信数据目录定位与 FileStorage 结构 | PRECEDENT | 社区广泛验证（腾讯官方"存储空间管理"功能确认 Cache 可清；聊天媒体删除影响与官方管理器一致），上线前逐版本夹具验证（ADPT-FR-005） |
| A-03 | Steam 多库与 shadercache | PRECEDENT | `libraryfolders.vdf` 公开文本格式；Steam 客户端自带"着色器预缓存"开关语义 |
| A-04 | Adobe Media Cache | OFFICIAL | [Adobe 媒体缓存管理文档](https://helpx.adobe.com/premiere-pro/using/media-cache.html) |
| A-05 | Docker/WSL vhdx 压缩 | OFFICIAL | [`Optimize-VHD`](https://learn.microsoft.com/powershell/module/hyper-v/optimize-vhd) / [diskpart compact vdisk](https://learn.microsoft.com/windows-server/administration/windows-commands/vdisk-compact)；微软 WSL 仓库公开指引 |
| A-06 | Windows Installer 孤儿包核对 | API | [`MsiEnumProductsW`](https://learn.microsoft.com/windows/win32/api/msi/nf-msi-msienumproductsw)、[`MsiGetProductInfoW`](https://learn.microsoft.com/windows/win32/api/msi/nf-msi-msigetproductinfow) |
| A-07 | NuGet/npm/pip/Gradle 缓存清理语义 | OFFICIAL | 见 R-04（各工具官方 clean/prune 命令） |
| A-08 | OneDrive 按需文件释放 | OFFICIAL | [Files On-Demand](https://support.microsoft.com/office/save-disk-space-with-onedrive-files-on-demand-for-windows-0e6860d3-d9f3-4971-b321-7092438fb38e) |

## S — 安全模型（对应 SAFE-FR）

| # | 需求 | 依据 | 参考 |
|---|------|------|------|
| S-01 | 回收站删除 | API | [`IFileOperation`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation)（`FOFX_RECYCLEONDELETE`） |
| S-02 | 同卷 O(1) 移动隔离 | API/BCL | [`MoveFileExW`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw)（同卷为改名操作）/ `File.Move` |
| S-03 | 路径真实目标解析 | API | [`GetFinalPathNameByHandleW`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew) |
| S-04 | 占用检测 | API | [Restart Manager](https://learn.microsoft.com/windows/win32/rstmgr/about-restart-manager) |
| S-05 | 服务停启 | BCL | [`ServiceController`](https://learn.microsoft.com/dotnet/api/system.serviceprocess.servicecontroller) |
| S-06 | 规则包签名 | BCL | [`ECDsa`（P-256）](https://learn.microsoft.com/dotnet/api/system.security.cryptography.ecdsa) — .NET 内置，无第三方依赖 |
| S-07 | UAC 按需提权 | OFFICIAL | [UAC 与 runas 动词](https://learn.microsoft.com/windows/win32/secauthz/user-account-control) |
| S-08 | 管道 ACL | BCL | [`PipeSecurity`](https://learn.microsoft.com/dotnet/api/system.io.pipes.pipesecurity) |
| S-09 | 客户端签名校验 | BCL | [`X509Certificate.CreateFromSignedFile`](https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates.x509certificate.createfromsignedfile) + WinVerifyTrust（[API](https://learn.microsoft.com/windows/win32/api/wintrust/nf-wintrust-winverifytrust)） |
| S-10 | 目录 ACL | BCL/API | [`FileSystemSecurity`](https://learn.microsoft.com/dotnet/api/system.security.accesscontrol.filesystemsecurity)、[`SetNamedSecurityInfoW`](https://learn.microsoft.com/windows/win32/api/aclapi/nf-aclapi-setnamedsecurityinfow) |
| S-11 | WAL 持久化 | BCL | `FileStream.Flush(flushToDisk: true)`（[文档](https://learn.microsoft.com/dotnet/api/system.io.filestream.flush)） |
| S-12 | 优雅关闭应用 | BCL | [`Process.CloseMainWindow`](https://learn.microsoft.com/dotnet/api/system.diagnostics.process.closemainwindow) |

## T — 技术栈（对应 05）

| # | 需求 | 依据 | 参考 |
|---|------|------|------|
| T-01 | .NET 8 LTS | OFFICIAL | [.NET 支持策略](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) |
| T-02 | WinUI 3 on Win10 1809+ | OFFICIAL | [Windows App SDK 系统要求](https://learn.microsoft.com/windows/apps/windows-app-sdk/system-requirements) |
| T-03 | CsWin32 | OFFICIAL | [microsoft/CsWin32](https://github.com/microsoft/CsWin32)（微软官方项目） |
| T-04 | StreamJsonRpc | OFFICIAL | [microsoft/vs-streamjsonrpc](https://github.com/microsoft/vs-streamjsonrpc)（微软官方项目） |
| T-05 | 高性能托管索引可行性 | PRECEDENT | .NET BCL 自身的 `System.IO.Enumeration`；多个 C# Everything 类开源索引器；`Span<T>`/`ArrayPool` 官方性能指南 |
| T-06 | 自包含发布/Trimming | OFFICIAL | [.NET 部署](https://learn.microsoft.com/dotnet/core/deploying/) |
| T-07 | VHDX 测试夹具 | OFFICIAL | [`New-VHD`](https://learn.microsoft.com/powershell/module/hyper-v/new-vhd)、[Mount-DiskImage](https://learn.microsoft.com/powershell/module/storage/mount-diskimage) |
| T-08 | UI 自动化测试 | PRECEDENT | FlaUI/WinAppDriver（基于微软 [UI Automation](https://learn.microsoft.com/windows/win32/winauto/entry-uiauto-win32)） |

## 维护规则

1. 新需求 PR 必须新增或引用本表条目，否则 CI 文档门禁失败
2. `PRECEDENT` 类条目在对应功能实现前必须补充夹具验证记录（升级为"已验证"）
3. 链接失效每季度巡检一次
