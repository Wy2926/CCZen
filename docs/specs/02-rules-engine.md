---
title: CCZen 清理规则引擎规范
description: 环境发现、候选生成、证据评分与风险分级的功能需求，以及规则包数据格式。全部推理本地执行，不依赖 LLM。
ms.topic: reference
ms.date: 2026-07-07
status: Draft v0.2
applies-to: Windows 10 版本 1809 及更高版本
---

# 清理规则引擎规范

规则引擎把"有经验的工程师判断哪些内容可以清理"的过程固化为可解释的本地推理管线：**环境发现 → 候选生成 → 证据评估 → 评分 → 风险分级 → 推荐输出**。全程离线、零 LLM、结果可审计。每个环节只使用公开文档记载的 API 或成熟先例（追溯表 06 条目 R-xx）。

## 设计原则

1. **不写死路径**：规则描述"符号化位置 + 模式 + 证据"，通过环境模型绑定到真实路径
2. **证据叠加**：单一信号不足以删除；多个独立信号叠加才提升置信度
3. **可再生性优先**：只推荐删除"删除后可被自动重建/不影响用户数据"的内容
4. **可解释**：每个推荐项展示命中的规则与信号
5. **规则即数据**：规则包为签名 JSON 文档（System.Text.Json + JSON Schema 校验），无代码执行能力

## 管线 1 — 环境发现（Environment Discovery）

| ID | 需求 | 实现依据 | 级别 |
|----|------|----------|------|
| RULE-FR-001 | **必须**通过 [`SHGetKnownFolderPath`](https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shgetknownfolderpath)（.NET: `Environment.GetFolderPath` + KNOWNFOLDERID 互操作）解析每个用户的 TEMP、LocalAppData、Downloads、Documents 等真实位置（应对文件夹重定向） | 公开 API | MUST |
| RULE-FR-002 | **必须**从注册表 [Uninstall 键](https://learn.microsoft.com/windows/win32/msi/uninstall-registry-key)（HKLM/HKCU × 64/32 位视图，`Microsoft.Win32.RegistryKey.OpenBaseKey`）构建已安装应用清单（名称、版本、InstallLocation、发布者） | 公开注册表约定 | MUST |
| RULE-FR-003 | **必须**通过 [`PackageManager.FindPackagesForUser`](https://learn.microsoft.com/uwp/api/windows.management.deployment.packagemanager) 枚举 MSIX/Store 应用 | 公开 WinRT API | MUST |
| RULE-FR-004 | **必须**采集环境变量与常见包管理器配置定位缓存重定向（`npm config get cache`、`PIP_CACHE_DIR`、`GRADLE_USER_HOME`、`CARGO_HOME`、`NUGET_PACKAGES` 等，均为各工具公开配置项） | 各工具官方文档 | MUST |
| RULE-FR-005 | **必须**用 `System.Diagnostics.Process.GetProcesses` 建立运行中进程快照（影响可清理性判定） | .NET BCL | MUST |
| RULE-FR-006 | **必须**采集卷信息（`DriveInfo`：类型、容量、剩余空间）；剩余空间只影响展示排序，**不得**放宽安全阈值 | .NET BCL | MUST |

环境模型输出符号绑定表：规则通过 `${TEMP}`、`${LOCALAPPDATA}`、`${app:WeChat.data_dir}` 引用位置，不出现字面绝对路径。

## 管线 2 — 候选生成（Candidates）

### 2.1 系统级已知类别（全部有微软官方清理通道或公开语义）

| ID | 类别 | 实现依据 | 级别 |
|----|------|----------|------|
| RULE-FR-010 | 用户/系统临时目录内容 | `GetTempPath` 语义；Windows 存储感知同样清理 | MUST |
| RULE-FR-011 | 回收站（各卷 `$Recycle.Bin`）：统计用 [`SHQueryRecycleBin`](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shqueryrecyclebin)，清空用 [`SHEmptyRecycleBin`](https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shemptyrecyclebin) | 公开 Shell API | MUST |
| RULE-FR-012 | Windows 更新缓存 `%WINDIR%\SoftwareDistribution\Download`：停 `wuauserv` 服务（`ServiceController`）后删除内容再恢复 | 微软官方排障文档记载的公开步骤 | MUST |
| RULE-FR-013 | 传递优化缓存：**必须**优先调用 [Delivery Optimization 的官方清理接口（`Delete-DeliveryOptimizationCache` PowerShell / IDO API）](https://learn.microsoft.com/windows/deployment/do/waas-delivery-optimization) | 官方 API | MUST |
| RULE-FR-014 | Windows.old / $WINDOWS.~BT：**必须**通过磁盘清理已注册处理程序通道（cleanmgr [SAGESET 自动化](https://learn.microsoft.com/windows-server/administration/windows-commands/cleanmgr) 或 Storage Sense 引导），不自行删除 | 官方通道 | MUST |
| RULE-FR-015 | 内存转储（`%WINDIR%\MEMORY.DMP`、`Minidump\*.dmp`）、WER 报告队列（ProgramData\Microsoft\Windows\WER） | cleanmgr 同类清理项 | MUST |
| RULE-FR-016 | 缩略图缓存（`%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db`） | cleanmgr 同类清理项 | MUST |
| RULE-FR-017 | 休眠文件：**必须**只引导用户执行 `powercfg /hibernate off`（官方命令），绝不直接删 hiberfil.sys | [powercfg 文档](https://learn.microsoft.com/windows-hardware/design/device-experiences/powercfg-command-line-options) | MUST |
| RULE-FR-018 | WinSxS：**必须**只通过 [DISM `StartComponentCleanup`](https://learn.microsoft.com/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder) 官方通道 | 官方文档 | MUST |

### 2.2 通用启发式（覆盖长尾软件）

| ID | 需求 | 级别 |
|----|------|------|
| RULE-FR-020 | **必须**支持"缓存形态目录"检测：路径段命中可更新词表（`cache/tmp/logs/GPUCache/ShaderCache/CrashDumps` 等），且位于 `${LOCALAPPDATA}`/`${TEMP}`/应用安装目录，而非用户文档区 | MUST |
| RULE-FR-021 | **必须**支持日志/转储文件形态检测：`*.log`、`*.tmp`、`*.dmp`、`*.etl`、`*.old`、`*.bak`（bak 风险档更高） | MUST |
| RULE-FR-022 | **必须**支持安装包残留检测：Downloads/TEMP 中的安装介质（exe/msi/zip），对应应用已安装且 N 天未访问 | MUST |
| RULE-FR-023 | **必须**支持孤儿目录检测：AppData/ProgramData 下归属应用不在已安装清单且 ≥ 90 天未修改（仅 T2 档） | MUST |
| RULE-FR-024 | **应**支持重复大文件报告：≥ 阈值同尺寸文件分段哈希（`System.Security.Cryptography.SHA256`/`XxHash64`（[System.IO.Hashing](https://learn.microsoft.com/dotnet/api/system.io.hashing)））确认；仅报告，保留哪份由用户决定 | SHOULD |
| RULE-FR-025 | 大而陈旧的文件**必须**只进入"大文件浏览器"供用户决策，永不自动推荐删除 | MUST |

### 2.3 应用适配器
见 [03-app-adapters.md](03-app-adapters.md)。Adapter 命中的路径接管通用规则（去重，Adapter 优先）。

## 管线 3 — 证据信号（Evidence）

每个候选收集以下信号，各输出 [0,1] 分值；全部为本地可计算量：

| 信号 | 计算方式 | 实现依据 |
|------|----------|----------|
| `location` | 位置语义强度（TEMP=1.0；LocalAppData 缓存命名=0.8；用户文档区=0） | 环境模型 |
| `regenerable` | 命中系统类别/Adapter 声明=高；未知=低 | 规则包数据 |
| `staleness` | 基于修改时间的衰减函数；LastAccess 仅在 `NtfsDisableLastAccessUpdate`（[fsutil behavior](https://learn.microsoft.com/windows-server/administration/windows-commands/fsutil-behavior)）显示启用更新时参与 | 索引数据 + 注册表 |
| `owner_state` | 已卸载 > 已安装未运行 > 运行中（运行中大幅降分） | RULE-FR-002/005 |
| `content_type` | 候选树内含文档/照片/工程文件等用户资产扩展名 → 一票降级 | 索引扩展名统计 |
| `lock_state` | [Restart Manager（`RmStartSession`/`RmGetList`）](https://learn.microsoft.com/windows/win32/api/restartmgr/nf-restartmgr-rmgetlist) 占用探测 → 降分 | 公开 API |
| `system_critical` | 命中保护清单（见 04）→ 一票否决 | 引擎硬编码 |

## 管线 4 — 评分与风险分级

综合置信分 = 规则包定义的加权组合，受一票否决/降级约束。输出四档：

| 档位 | 含义 | 默认行为 |
|------|------|----------|
| **T0 安全** | 官方语义/官方通道的可再生内容 | 一键清理默认勾选 |
| **T1 推荐** | 多信号高置信启发式命中 | 展示并默认勾选，逐组可看"为什么" |
| **T2 谨慎** | 中置信（孤儿目录、`*.bak`、聊天媒体等） | 展示但默认不勾选，逐项确认 |
| **T3 专家** | 大文件/重复文件/系统级引导操作 | 只读展示 + 官方通道引导，绝不进入一键清理 |

硬性规则：
- 任何删除都走隔离区/回收站（见 04）
- 用户设置只能更保守；**不得**把 T2/T3 提升为自动清理
- 空间紧张不放宽安全阈值

## 规则包格式

- 格式：JSON（UTF-8），随包分发 [JSON Schema](https://json-schema.org/)，引擎加载前 **必须** 校验（System.Text.Json + JsonSchema.Net 或等价实现）
- 组成：内置基线包（随安装分发）+ 可选更新包（ECDSA P-256 签名校验，见 04-SAFE-FR-030）
- 每条规则字段：`id`、`tier`/`tierCap`、`targets`（符号化 glob）、`match`（词表/扩展名引用）、`signals` 权重、`preconditions`（如 `service:wuauserv`）、`action`（引擎内置动作集枚举：`quarantine`/`recycle`/`delete-contents`/`invoke-official:<channel>`）、`explain` 文案
- 词表（缓存词、用户资产扩展名，多语言）同为规则包数据
- Schema 版本化；引擎兼容 N-1 版本

示例（示意）：

```json
{
  "id": "generic-app-cache",
  "tierCap": "T1",
  "targets": ["${LOCALAPPDATA}/**"],
  "match": { "dirNameLexicon": "cache_words", "excludeContent": "user_asset_exts" },
  "signals": { "location": 0.8, "regenerable": 0.7 },
  "action": "quarantine",
  "explain": "位于本地应用数据区的缓存目录，删除后应用会自动重建"
}
```

## 输出契约

引擎向 UI 输出按类别分组的候选树，每项含：路径、逻辑/分配大小、档位、置信分、命中规则 ID、解释文案、前置条件、执行计划。

## 测试要求

- 规则包 Schema 校验测试 + golden 测试：固定夹具文件树 → 断言推荐集与档位完全一致
- 对抗夹具：用户照片放入名为 cache 的目录 → 必须被 `content_type` 降级
- 中文用户名、Known Folders 重定向、便携版应用环境矩阵
- 属性测试（FsCheck）：随机文件树输入不崩溃、保护路径永不出现在 T0/T1

## 另请参阅

- [存储感知（Storage Sense）](https://support.microsoft.com/windows/manage-drive-space-with-storage-sense-654f6ada-7bfc-45e5-966b-e24aded96ad5) — 本引擎 T0 类别与其清理范围对齐
- [cleanmgr 命令](https://learn.microsoft.com/windows-server/administration/windows-commands/cleanmgr)
- 06 追溯表条目 R-01 ~ R-20
