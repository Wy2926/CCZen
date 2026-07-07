---
title: CCZen 应用适配层规范
description: 主流应用（IM、浏览器、开发工具、游戏平台、创意软件）声明式清理适配清单的格式与首发范围。
ms.topic: reference
ms.date: 2026-07-07
status: Draft v0.2
applies-to: Windows 10 版本 1809 及更高版本
---

# 应用适配层规范

通用启发式解决"覆盖面"，Adapter 解决"精细度"。Adapter 是**声明式清单**（规则包的一部分，签名分发、可热更新），不是硬编码。每个 Adapter 先声明"如何在这台机器上发现该应用"，再声明"发现后有哪些可清理项"。所有清理项**必须**满足 00 的可行性约定：路径语义有官方文档、厂商公告或强社区共识依据，登记于 06 追溯表（A-xx）。

## Adapter 清单格式

| ID | 需求 | 级别 |
|----|------|------|
| ADPT-FR-001 | Adapter **必须**为独立 JSON 文件，含 `id`、`name`、`category`、`detect`、`items`、`verifiedVersions` 字段，随规则包 Schema 校验 | MUST |
| ADPT-FR-002 | `detect` **必须**支持多路发现并全部输出到环境模型：Uninstall 注册表模式（RULE-FR-002）、已知路径模式探测、进程名（RULE-FR-005）、MSIX 包名（RULE-FR-003）、`configProbe`（读取应用自身公开配置文件/注册表值定位被用户迁移的数据目录） | MUST |
| ADPT-FR-003 | `configProbe` 只允许**读取**声明的文件/注册表值并按声明的正则/JSON 路径提取，不允许执行任何代码 | MUST |
| ADPT-FR-004 | 每个 `item` **必须**含 `tier`、`explain`、枚举模式（符号化 glob 或按时间分桶）、`preconditions`（如"应用未运行"，用 Restart Manager 校验） | MUST |
| ADPT-FR-005 | 应用版本不在 `verifiedVersions` 范围时对应 items **必须**自动降一档 | MUST |
| ADPT-FR-006 | Adapter 命中的路径**必须**接管通用启发式（同一路径不重复报告，Adapter 优先） | MUST |
| ADPT-FR-007 | 进入 T0/T1 的 item **必须**在 06 追溯表登记依据（官方文档 / 厂商支持页 / 强共识——如多个主流清理工具的公开行为） | MUST |

### 数据目录迁移探测（关键通用性要求）

微信/QQ/网易云音乐等常被用户把数据目录迁移到非系统盘。Adapter **必须**通过 `configProbe` 读取应用自身记录的路径（示例：微信 `HKCU\Software\Tencent\WeChat\FileSavePath` 及 `%APPDATA%\Tencent\WeChat\All Users\config\3ebffe94.ini` 中记录的存储路径——均为社区广泛验证的公开位置），而不是假设默认路径。探测失败时回退到默认路径模式匹配；仍未命中则该 Adapter 不激活（由通用启发式兜底）。

## 首发 Adapter 集（v1）

> 下表"档位"指该项最高档；实际档位仍受 02 评分与一票降级约束。每项依据登记在 06（A-xx）。

### 即时通讯

| 应用 | 项目 | 档位 |
|------|------|------|
| 微信 / 企业微信 | `FileStorage\Cache`、小程序缓存（`WMPF`/`Applet`）、更新包残留：T1；`FileStorage\Image/Video/File` 聊天媒体按**月份分桶**展示：T2（明确提示"删除后聊天记录内无法查看原图/视频"） | T1/T2 |
| QQ / TIM | 图片缓存、群文件缓存、更新残留 | T1/T2 |
| 钉钉 / 飞书 | 文件缓存、会议录制缓存 | T1/T2 |
| Telegram Desktop | `tdata\user_data` 媒体缓存（应用内亦提供官方清理入口，语义明确） | T1 |

### 浏览器（Chromium 系 / Firefox）

| 项目 | 说明 | 档位 |
|------|------|------|
| HTTP Cache、`Code Cache`、`GPUCache`、Service Worker CacheStorage | 按 **profile** 枚举（`User Data\*\`），目录语义为 Chromium 公开源码结构 | T0/T1 |
| 旧版本残留（`Application\<old_version>`） | 版本目录与当前运行版本比对 | T1 |
| **绝不触碰** | Bookmarks、History、Cookies、Login Data、扩展数据——列入保护清单（04） | — |
| 前置条件 | 浏览器运行中只提示不清理（Restart Manager 判定） | — |

### 开发工具

| 生态 | 项目 | 依据 | 档位 |
|------|------|------|------|
| npm / pnpm / yarn | 全局缓存目录（`npm config get cache`）；pnpm 用官方 `pnpm store prune` 语义 | 各工具官方文档 | T1 |
| pip / conda | `pip cache dir`、conda `pkgs` | 官方文档（`pip cache purge`、`conda clean`） | T1 |
| Gradle / Maven | `~/.gradle/caches/modules-2`、旧 wrapper 发行版；`~/.m2/repository` 仅报告 | Gradle/Maven 官方文档 | T1/T2 |
| NuGet | 全局包缓存（官方 `dotnet nuget locals` 语义） | [官方文档](https://learn.microsoft.com/nuget/consume-packages/managing-the-global-packages-and-cache-folders) | T1 |
| 陈旧项目 node_modules | 含 `.git` 的项目根、≥90 天未修改 → 仅 T2 展示 | 启发式（02） | T2 |
| Docker Desktop | 引导官方 `docker system prune`；WSL2 `ext4.vhdx` 空间不回收 → 引导官方 [`Optimize-VHD`/diskpart compact vdisk](https://learn.microsoft.com/windows-server/administration/windows-commands/vdisk-compact) 流程 | 官方命令 | T3 |
| Visual Studio / JetBrains / VS Code | 旧版本缓存目录（JetBrains 按版本号目录识别已不存在版本）、日志、崩溃转储 | 厂商公开目录约定 | T1 |
| Windows Installer 孤儿缓存 | `%WINDIR%\Installer` 中未被任何已安装产品引用的 msi/msp（通过 [MSI API `MsiEnumProducts`/`MsiGetProductInfo`](https://learn.microsoft.com/windows/win32/api/msi/nf-msi-msienumproductsw) 严格核对引用后仅列 T2） | 公开 MSI API | T2 |

### 游戏平台与显卡

| 平台 | 项目 | 依据 | 档位 |
|------|------|------|------|
| Steam | `shadercache`、`downloading` 残留；多库位置解析 `libraryfolders.vdf`（公开文本格式） | Valve 公开目录结构 | T1 |
| Epic / EA / Ubisoft | 下载暂存、日志 | 各平台公开目录 | T1 |
| 米哈游/网易系启动器 | 已完成安装的更新包残留 | 启动器公开下载目录 | T1 |
| NVIDIA / AMD | `DXCache`/`GLCache`/`NV_Cache` 着色器缓存；NVIDIA 旧驱动安装残留（`Displ.Driver` 版本目录） | 厂商公开缓存目录 | T1 |

### 创意与办公

| 应用 | 项目 | 依据 | 档位 |
|------|------|------|------|
| Adobe Premiere / AE | Media Cache Files（Adobe 官方提供"清理媒体缓存"功能，语义明确） | [Adobe 官方文档](https://helpx.adobe.com/premiere-pro/using/media-cache.html) | T1 |
| DaVinci / 剪映 | 渲染缓存、代理文件（提示影响工程打开速度） | 厂商公开设置项 | T2 |
| Office | 更新缓存、崩溃转储 | 公开目录 | T1 |
| 网盘/下载（OneDrive、百度网盘、迅雷） | 本地缓存、已完成任务临时分片：T1；OneDrive"仅在线可用"释放引导官方 [Files On-Demand](https://support.microsoft.com/office/save-disk-space-with-onedrive-files-on-demand-for-windows-0e6860d3-d9f3-4971-b321-7092438fb38e)：T3 | 官方功能 | T1/T3 |

### 安全软件全家桶

360/腾讯电脑管家等自身的备份与下载目录：仅 T2 展示（避免互删争议）。

## 治理

| ID | 需求 | 级别 |
|----|------|------|
| ADPT-FR-010 | CI **必须**对 Adapter 做 Schema、tier 合法性、explain 完整性、06 依据登记的门禁校验 | MUST |
| ADPT-FR-011 | 遥测默认关闭；用户自愿开启时仅上报规则命中统计（无路径、无文件名） | MUST |

## 测试要求

- 每个 Adapter 三套夹具：默认路径版、迁移路径版、便携版 → golden 断言
- 应用运行中场景：断言只提示不删除
- 版本升级场景：目录结构变化后不误报、`verifiedVersions` 外自动降档
