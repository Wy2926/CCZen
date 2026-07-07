# CCZen

面向 Windows 10+ 的通用磁盘清理软件（**.NET 8 / C#**）—— Everything 级秒扫大文件/大文件夹 + 无 LLM 的本地智能清理推荐。

当前阶段：**规格驱动（Spec-Driven），尚无实现代码。** Spec 遵循微软 Learn 文档风格，所有需求必须在可行性追溯表登记实现依据。

## 文档导航

- [AGENTS.md](AGENTS.md) — 开发准则（继承 ECC）与领域红线
- [docs/specs/00-overview.md](docs/specs/00-overview.md) — 规格总览与导航
  - 01 扫描引擎（MFT/USN 秒级索引）
  - 02 清理规则引擎（发现→证据→评分→分级）
  - 03 应用适配层（微信/浏览器/开发工具/游戏平台…）
  - 04 安全模型（隔离区/撤销/保护清单/签名规则包）
  - 05 技术架构（.NET 8 / WinUI 3）与验收标准
  - 06 可行性追溯表（需求 → 公开 API/官方文档/成熟先例）
- `docs/rules/` — 通用工程准则（来自 ECC rules/common）
- `.agents/skills/` — 从 ECC 引入的开发技能，供 AI agent 在本仓库工作时使用
