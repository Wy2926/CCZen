# CCZen — Agent Instructions

CCZen 是一款面向 Windows 10+ 的通用磁盘清理软件。本仓库当前处于 **规格驱动（Spec-Driven）** 阶段：先完成 spec，再进入实现。

本仓库沿用 **ECC（Everything Claude Code）开发准则**（来源：https://github.com/affaan-m/ECC）。

## 核心原则（继承自 ECC）

1. **Agent-First** — 领域任务交给专门的 agent/skill 处理
2. **Test-Driven** — 先写测试再写实现，覆盖率 ≥ 80%
3. **Security-First** — 安全不可妥协；校验一切输入
4. **Immutability** — 创建新对象而非修改旧对象
5. **Plan Before Execute** — 复杂功能先规划再动手

## 本仓库的技能（.agents/skills/）

从 ECC 精选并纳入本仓库的技能，任何 agent 在相关任务开始时应激活：

| Skill | 用途 |
|-------|------|
| intent-driven-development | 规格/意图驱动开发流程 |
| plan-orchestrate | 将计划分解为可执行步骤链 |
| coding-standards | 通用编码规范 |
| tdd-workflow | TDD 红-绿-重构流程 |
| verification-loop | 交付前自验证闭环 |
| error-handling | 错误处理模式 |
| security-review | 安全审查（删除类软件尤其关键） |
| latency-critical-systems | 低延迟系统设计（秒级扫描的核心） |
| dotnet-patterns / csharp-testing | .NET/C# 技术栈（全栈选型，见 specs/05） |
| e2e-testing | 端到端测试 |
| windows-desktop-e2e | Windows 桌面应用 E2E 测试 |
| deployment-patterns | 发布/部署模式 |

通用准则细则见 `docs/rules/`（来自 ECC rules/common）。

## 领域红线（CCZen 特有，任何时候不可违反）

- **绝不静默永久删除**：所有删除默认进回收站/隔离区，可撤销
- **绝不删除用户文档类文件**（文档/照片/视频等）除非用户明确逐项确认
- **不依赖任何 LLM**：清理智能全部由本地规则引擎 + 启发式评分实现
- **不写死路径**：一切清理目标通过"发现 + 证据 + 评分"机制得出，主流应用的特殊适配以声明式 Adapter 清单表达
- **禁止空想需求**：任何需求必须在 `docs/specs/06-feasibility-matrix.md` 登记实现依据（公开 API/官方文档/成熟先例），否则不得进入 spec
- **无遥测默认开启**；规则包必须签名校验后才可加载

## 工作流

1. Spec 先行：功能变更先更新 `docs/specs/`（微软 Learn 文档风格：元数据块、RFC 2119 需求表、需求 ID、"另请参阅"）再实现；新增需求同步登记 06 追溯表
2. TDD：先测试后实现
3. 提交格式：`<type>: <description>`（feat/fix/refactor/docs/test/chore/perf/ci）
4. PR 必须附带测试计划与规格引用
