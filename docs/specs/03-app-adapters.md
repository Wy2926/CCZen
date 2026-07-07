# Spec 03 — 应用适配层（主流应用特殊适配）

> 通用启发式解决"覆盖面"，Adapter 解决"精细度"。Adapter 是**声明式清单**（规则包的一部分，可热更新），不是硬编码：每个 Adapter 先声明"如何在这台机器上发现该应用"，再声明"发现后有哪些可清理项"。

## 1. Adapter 清单结构

```
adapter:
  id / 名称 / 分类（IM、浏览器、开发、游戏、创意…）
  detect:      # 多路发现，命中任一即绑定，全部输出到环境模型
    - uninstall_registry: 显示名/发布者模式
    - known_path: ${LOCALAPPDATA}/... 等模式探测
    - process_name / MSIX 包名
    - config_probe: 读应用自身配置以定位"数据目录被用户迁移"的情况（如微信文件夹搬到 D 盘）
  items:       # 可清理项，每项含 tier、解释、枚举器、前置条件
  preconditions: 运行中处理策略（提示退出 / Restart Manager）
```

关键点：**detect 支持数据目录重定向**。微信/QQ/网易云等常被用户迁移到非默认盘，Adapter 必须读取应用配置或注册表定位真实数据目录，而不是假设默认路径。

## 2. 首发 Adapter 集（v1 范围）

### 2.1 即时通讯（国内环境重灾区）
| 应用 | 项目（示例） | 档位 |
|------|--------------|------|
| 微信 / 企业微信 | 图片/视频/文件缓存按**月份分桶**展示（`FileStorage/Cache`、`Video`、`Image`）；小程序缓存；更新包残留 | 缓存 T1；聊天媒体 T2（按月勾选，明确提示"删除后聊天记录内无法再查看原图/视频"） |
| QQ / TIM | 群文件缓存、图片缓存、更新残留 | 同上 |
| 钉钉 / 飞书 | 会议录制缓存、文件缓存 | T1/T2 |
| Telegram / WhatsApp | media cache | T1/T2 |

### 2.2 浏览器
Chrome / Edge / Firefox / 360 / QQ 浏览器：
- 按 **profile** 枚举（支持多用户配置）：HTTP 缓存、Code Cache、GPUCache、Service Worker 缓存、旧版本残留（Edge/Chrome 的 `Application/<old_version>`）→ T0/T1
- **绝不触碰**：书签、历史、Cookies、密码、扩展数据（保护清单硬编码）
- 浏览器运行中 → 只提示，不清理

### 2.3 开发工具（开发者机器的最大金矿）
| 生态 | 项目 | 档位 |
|------|------|------|
| npm/yarn/pnpm | 全局缓存（`npm cache`、pnpm store 未引用包 via prune 语义）；**过期项目的 node_modules**（配套 .git 仓库、≥90 天未动的项目列为 T2 展示） | T1 / T2 |
| pip/conda | wheel 缓存、conda pkgs | T1 |
| Gradle/Maven | caches/modules-2、旧 wrapper 发行版 | T1 |
| cargo | registry cache/src | T1 |
| NuGet | 全局包缓存 | T1 |
| Docker Desktop | 提示走 `docker system prune` 语义 + **WSL2 vhdx 压缩引导**（ext4.vhdx 不回收问题，T3 引导式操作） | T3 |
| IDE（VS/JetBrains/VSCode） | 旧版本缓存目录、日志、崩溃转储；JetBrains 按版本号识别**已不存在版本**的缓存 | T1 |
| Windows SDK/驱动 | Installer 缓存孤儿包（严格校验 msi/msp 引用后才列 T2） | T2 |

### 2.4 游戏平台
| 平台 | 项目 | 档位 |
|------|------|------|
| Steam | shader cache、旧安装器残留、workshop 孤儿内容；`libraryfolders.vdf` 解析多库位置 | T1 |
| Epic/EA/Ubisoft | 下载暂存、日志 | T1 |
| 米哈游系/网易系启动器 | 更新包残留（下载完成已安装的 zip/分卷） | T1 |
| NVIDIA/AMD | DXCache/GLCache、驱动安装残留（`NVIDIA/Displ.Driver` 旧版本） | T1 |

### 2.5 创意与办公
- Adobe 系（Premiere/AE）：Media Cache Files（T1，业界公认可再生）
- 达芬奇/剪映：渲染缓存、代理文件（T2，提示影响工程打开速度）
- Office：更新缓存、崩溃转储（T1）
- 网盘类（OneDrive/百度网盘/夸克/迅雷）：本地缓存、已完成任务的临时分片（T1）；OneDrive 提示"仅在线可用"释放（T3 引导）

### 2.6 系统厂商全家桶
360/腾讯电脑管家等自身的备份与下载目录（T2，避免互删争议，仅展示）。

## 3. 通用回退保证

未被任何 Adapter 覆盖的应用，仍由 02 的通用启发式兜底（cache 形态目录、日志、孤儿目录）。Adapter 命中时**接管**对应路径，避免同一路径被通用规则和 Adapter 重复报告（Adapter 优先级更高）。

## 4. Adapter 治理

- 每个 Adapter 是独立文件，社区/团队可增改；CI 对 schema、tier 合法性、explain 完整性做门禁
- Adapter 声明 `verified_versions`：应用大版本变更目录结构时，未验证版本自动降一档
- 数据来源标注：每个清理项注明依据（官方文档/社区共识/逆向确认），进入 T0/T1 必须有官方或强共识依据
- 遥测缺省关闭；若用户自愿开启"匿名规则效果反馈"，仅上报规则命中统计（无路径、无文件名）

## 5. 测试要点

- 每个 Adapter 一套目录夹具（含默认路径版 + 迁移路径版 + 便携版）做 golden 断言
- 应用运行中场景：断言只提示不删除
- 版本升级场景：目录结构变化后不误报、自动降档
