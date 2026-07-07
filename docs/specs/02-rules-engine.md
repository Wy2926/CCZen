# Spec 02 — 清理规则引擎（无 LLM 的"清理智能"）

> 核心思想：把有经验的工程师/Agent 判断"这个能删"的思考过程，固化为**可解释的本地推理管线**：环境发现 → 候选生成 → 证据信号 → 置信度评分 → 风险分级。全程离线、零 LLM、结果可审计。

## 1. 设计原则

1. **不写死路径**：规则描述的是"模式 + 证据"，不是绝对路径。同一条规则要在中文/英文系统、C 盘装 D 盘装、便携版安装等环境下都成立
2. **证据叠加**：单一信号（如文件夹名叫 cache）不足以删除；多个独立信号叠加才提升置信度
3. **可再生性优先**：只推荐删除"删了会被自动重建/不影响数据"的内容
4. **可解释**：每个推荐项都能展示"为什么推荐"（命中的规则与信号）
5. **规则包与代码分离**：规则是签名的数据文件，可独立更新迭代（见 04 签名要求）

## 2. 管线总览

```
[环境发现 Discovery] → [候选生成 Candidates] → [证据评估 Evidence]
       → [评分 Scoring] → [风险分级 Tiering] → [推荐输出 + 解释]
```

## 3. 环境发现（Discovery）

在扫描索引就绪后，先"认识这台电脑"，产出 **环境模型**：

| 来源 | 得到什么 |
|------|----------|
| Known Folders API（`SHGetKnownFolderPath`） | 各用户 TEMP、LocalAppData、Downloads、Documents 等真实位置（应对重定向） |
| 注册表 Uninstall 键（HKLM/HKCU、含 WOW6432Node）+ MSIX 包列表 | 已安装应用清单：名称、版本、安装路径、发布者 |
| 环境变量 / 常见配置文件 | JAVA_HOME、CARGO_HOME、npm prefix、pip cache dir 等开发环境重定向 |
| 服务/进程快照 | 哪些应用正在运行（影响可清理性与锁判断） |
| 卷信息 | 各卷类型、剩余空间（影响推荐激进程度的展示，不影响安全阈值） |
| 用户画像启发（本地统计） | 是否开发者（存在 .git/node_modules/SDK）、是否游戏玩家（Steam/Epic）等，仅用于结果分组展示 |

环境模型是后续一切规则的**变量绑定表**：规则引用 `${TEMP}`、`${LOCALAPPDATA}`、`${app:WeChat.install_dir}` 等符号，而非字面路径。

## 4. 候选生成（Candidates）

三类生成器并行，全部在内存索引上运行（见 01 §4）：

### 4.1 系统级已知类别（规则包内置，微软公开语义）
临时目录、回收站（各卷 `$Recycle.Bin`）、Windows Update 缓存（SoftwareDistribution\Download）、Delivery Optimization、Windows.old、内存转储（MEMORY.DMP、Minidump）、WER 报告、缩略图/图标缓存、字体缓存、事件日志归档、Prefetch、Windows 升级残留（$WINDOWS.~BT）、休眠文件与页面文件（**只提示不代删**，走系统 API 建议）、WinSxS（**只通过 DISM StartComponentCleanup 通道**，绝不直接删文件）。

### 4.2 通用启发式（覆盖长尾软件，"Agent 直觉"的固化）

对索引做模式扫描，产出"疑似可清理"候选：

- **缓存形态目录**：路径段命中 `cache|caches|tmp|temp|logs|log|crash|dumps|pending|staging|GPUCache|Code Cache|ShaderCache|thumbnails` 等词表（词表在规则包中可更新），且位于 `%LOCALAPPDATA%`/`%TEMP%`/应用安装目录下，而非用户文档区
- **日志/转储文件形态**：`*.log`、`*.log.N`、`*.dmp`、`*.etl`、`*.tmp`、`*.old`、`*.bak`（bak 类风险级更高）
- **安装包残留**：Downloads 与 TEMP 中的 `*.exe|*.msi|*.zip` 安装介质，且对应应用已在环境模型中显示已安装、文件超过 N 天未访问
- **孤儿数据**：`%APPDATA%`/`%LOCALAPPDATA%`/ProgramData 下的应用目录，其归属应用**不在已安装清单**且目录 ≥ 90 天未修改 → "卸载残留"候选（低置信，需用户确认级别）
- **重复大文件**：对 ≥ 阈值（默认 256 MB）的同尺寸文件做分段哈希（头/中/尾采样 → 全量确认），报告重复组（仅报告，删除永远由用户选择保留哪份）
- **陈旧大文件**：大文件 + 长期未访问，仅进入"大文件浏览器"供用户决策，**永不自动推荐删除**

### 4.3 应用适配器（见 03）
Adapter 对主流应用产出更精细、带业务语义的候选（如"微信 2023 年以前的聊天图片缓存"）。

## 5. 证据信号（Evidence）

每个候选收集一组独立信号，各信号输出 [0,1] 分值：

| 信号 | 说明 |
|------|------|
| `location` | 所在位置的语义强度（TEMP=1.0，LocalAppData 下 cache 命名=0.8，用户文档区=0） |
| `regenerable` | 可再生性：已知缓存语义/命中 Adapter 声明=高；未知=低 |
| `staleness` | 年龄：基于修改/访问时间的衰减函数（注意 NTFS 默认可能关闭 LastAccess 更新，需检测 `NtfsDisableLastAccessUpdate` 并降权该信号） |
| `owner_state` | 归属应用状态：已卸载 > 已安装未运行 > 正在运行（运行中大幅降分并标记"需退出应用"） |
| `content_type` | 内容扩展名画像：候选目录内若含文档/照片/工程文件等"用户资产"扩展名 → 一票降级 |
| `lock_state` | 文件占用探测（open 探测 + Restart Manager）：被占用 → 降分 |
| `system_critical` | 命中保护清单（Windows、Program Files 核心、驱动、用户配置文件根等）→ 一票否决 |
| `adapter_confidence` | Adapter 显式声明的项获得基础高置信 |

## 6. 评分与风险分级（Tiering）

综合置信分 = 加权组合（权重在规则包中调优），并受"一票否决/一票降级"约束。输出四档：

| 档位 | 含义 | 默认行为 |
|------|------|----------|
| **T0 安全** | 微软/Adapter 明确语义的可再生内容（TEMP、回收站按用户选择、WU 缓存…） | 进入"一键清理"默认勾选 |
| **T1 推荐** | 多信号高置信启发式命中（如通用 cache 目录、旧安装包） | 展示并默认勾选，逐组可看"为什么" |
| **T2 谨慎** | 中置信（孤儿目录、`*.bak`、聊天媒体等用户可能在意的内容） | 展示但**默认不勾选**，需逐项确认 |
| **T3 专家** | 仅供浏览决策（大文件、重复文件、休眠/页面文件建议、WinSxS） | 只读展示 + 引导操作，绝不出现在一键清理 |

硬性规则：
- 任何档位删除都走隔离区/回收站（见 04）
- 阈值可调，但用户设置**只能更保守，不能把 T2/T3 提升为自动清理**
- 空间紧张不放宽安全阈值（避免"越缺空间越乱删"的反模式）

## 7. 规则包 DSL

规则以声明式文档（TOML/JSON，schema 校验）分发，示例形态（示意，非最终 schema）：

```toml
[rule.generic-app-cache]
tier_cap = "T1"                 # 该规则最高只能给到 T1
targets  = ["${LOCALAPPDATA}/**"]
match    = { dir_name_lexicon = "cache_words", exclude_content = "user_asset_exts" }
signals  = { location = 0.8, regenerable = 0.7 }
explain  = "位于本地应用数据区的缓存目录，删除后应用会自动重建"

[rule.windows-update-cache]
tier     = "T0"
targets  = ["${WINDIR}/SoftwareDistribution/Download/*"]
preconditions = ["service_stopped_or_stoppable:wuauserv"]
action   = { stop_service = "wuauserv", delete = "contents", restart_service = true }
```

要求：
- schema 版本化；引擎向后兼容 N-1 版本规则包
- 规则包 = 内置基线包（随安装分发）+ 可选更新包（签名校验，见 04）
- 词表（cache 词、用户资产扩展名表）也是规则包数据，可随迭代扩充多语言词汇
- 每条规则必须带 `explain` 文案与 `tier`/`tier_cap`

## 8. 输出契约

规则引擎向 UI 输出结构化结果：按类别分组的候选树，每项含：路径、逻辑/分配大小、档位、置信分、命中规则 ID、解释文案、前置条件（如"需退出微信"）、执行计划（删除/停服务后删/调用系统 API）。

## 9. 测试要点

- 规则包 schema 校验 + golden 测试：固定夹具文件树 → 断言推荐集合与档位完全一致
- 对抗夹具：把用户照片放进名为 cache 的目录 → 必须被 `content_type` 降级
- 中文/多语言用户名、重定向的 Known Folders、便携版应用环境矩阵
- 模糊测试：随机文件树输入不得崩溃、不得产出 T0/T1 的保护路径项
