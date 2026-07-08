---
title: 方案 B — 索引统一合并实施计划
description: 将智能清理与大文件搜索收敛为「一次建索引、多处内存查询」的目标架构。
ms.date: 2026-07-08
status: Approved
parent-specs:
  - docs/specs/01-scan-engine.md
  - docs/specs/02-rules-engine.md
  - docs/specs/05-architecture.md
---

# 方案 B — 索引统一合并实施计划

## 目标

消除规则引擎对 AppData/TEMP 的重复目录遍历；`RecommendAsync` 与 `SearchAsync` 共享同一 `FileSystemIndex`；对齐 `SCAN-FR-022` / `RULE-FR-026` / `RULE-FR-027`。

## 目标架构

```
CCZen.App (Cleaner + Search)
        │ IEngineClient
EngineRpcServer
  EnsureIndexAsync ──► FileSystemIndex (shared)
        │
        ├── IIndexQuery ──► RuleEngine / AdapterEngine
        └── Search / Top-N queries
```

## 完成定义（Exit Criteria）

| ID | 标准 | 验证 |
|----|------|------|
| AC-B01 | `RuleEngine`/`AdapterEngine` 无 `Directory.Enumerate*` | `rg EnumerateFiles src/CCZen.Engine/Rules` 零命中 |
| AC-B02 | `RecommendAsync` 后 `_index != null` | `EngineRpcTests` |
| AC-B03 | 连续 Clean+Search 仅一次全量扫 | 集成测试 |
| AC-B04 | Parity 推荐集合与旧 walk 实现一致 | `IndexDrivenRuleParityTests` |
| AC-B05 | 子树查询 < 100 ms @ 400 万节点 | BenchmarkDotNet |
| AC-B06 | 规格 01/02/06 需求 ID 完整 | 文档 CI |
| AC-B07 | 删除仍仅经 `CleanupPlanner` + 隔离 | security-review |

## Phase 概览

| Phase | 内容 | 工期 | PR |
|-------|------|------|-----|
| **0** | 规格 + ADR + 06 追溯 | 1–2 d | PR-1 |
| **1** | 索引 LastWrite + cache v2 | 3–4 d | PR-2 |
| **2** | `IIndexQuery` API | 4–5 d | PR-3 |
| **3** | `RuleEngine` 索引化 | 2–3 d | PR-4 |
| **4** | `AdapterEngine` 索引化 | 1–2 d | PR-4 |
| **5** | `EngineRpcServer` 编排 + 并发 | 2 d | PR-5 |
| **6** | UI/UX（Cleaner 索引状态） | 1–2 d | PR-5 |
| **7** | CLI 对齐 + dead code 清理 | 1 d | PR-6 |
| **8** | Parity / 基准 / security review | 2–3 d | PR-6 |

**总工期**：约 15 工作日（1 人）

---

## Phase 0 — 规格与追溯 ✅ 本 PR

- [x] Task 0.1 — 更新 `01-scan-engine.md`（SCAN-FR-026..029）
- [x] Task 0.2 — 更新 `02-rules-engine.md`（RULE-FR-026..027）
- [x] Task 0.3 — 登记 `06-feasibility-matrix.md`（F-13..F-14, R-15）
- [x] Task 0.4 — ADR `docs/adr/ADR-001-index-cache-v2.md`

**Gate**：本 Phase 合并后才开始 Phase 1 生产代码。

---

## Phase 1 — 索引数据模型扩展 ✅

- [x] Task 1.1 — MFT 解析 LastWriteTime（`TryGetFileMetadata`）
- [x] Task 1.2 — `IndexBuilder` / `FileSystemIndex` 时间字段 + 子树 max-mtime
- [x] Task 1.3 — 缓存格式 v2（`CacheVersion = 2`；v1 拒绝加载）
- [x] Task 1.4 — `UsnJournalScanner` / `FallbackScanner` 写入 mtime

---

## Phase 2 — `IIndexQuery` 查询层 ✅

- [x] Task 2.1 — `IIndexQuery` + `SubtreeStats`（`Index/IIndexQuery.cs`）
- [x] Task 2.2 — `IndexQuery` 路径前缀解析（懒建 `_pathToNode`）
- [x] Task 2.3 — `FindDirectoriesByName`（BFS + maxDepth）
- [x] Task 2.4 — `FindFilesByExtension`（递归/非递归）
- [x] Task 2.5 — `ExpandGlob`（中间 `\*` / 尾部 `*`）
- [x] Task 2.6 — `SubtreeContainsExtension` + `GetSubtreeStats`
- [x] `IndexQueryTests`（9 项）

---

## Phase 3 — RuleEngine 索引化 ✅

- [x] 注入 `IIndexQuery`；移除目录 walk
- [x] 三类 evaluate 改为索引查询
- [x] `RuleEngineTests` 经 `TestIndexFactory` 建索引

---

## Phase 4 — AdapterEngine 索引化 ✅

- [x] `MeasureTree` 改为读 `GetSubtreeStats`（无磁盘 walk）
- [x] `ExpandItemTarget` → `ExpandGlob` / `TryResolvePrefix`

---

## Phase 5 — 引擎编排 ✅

- [x] `RecommendAsync` → `EnsureIndex` + 共享 `IndexQuery`
- [x] `getScanRoot` 可注入（测试）
- [x] `ReaderWriterLockSlim`（SCAN-FR-023）
- [x] 索引状态经现有 `GetStatusAsync` 暴露（无需单独 RPC）

---

## Phase 6 — UI ✅

- [x] `CleanerViewModel` ScanPhases 含索引步骤
- [x] `IndexStatusFormatter` + Cleaner/Search 页索引状态绑定
- [x] Search 索引已暖时显示「增量刷新」提示

---

## Phase 7 — 清理 ✅

- [x] 移除 `EngineRpcServer.NormalizeScanRoot`（未使用）
- [x] `CCZen.Cli recommend` 已经 `EvaluateAll()` 先扫卷再索引查询
- [x] `FallbackScanner` 不存在根路径返回空索引（不抛异常）
- [x] `EngineRpcServer` 注入 `createScanner`（测试计数扫描次数）

---

## Phase 8 — 验证 ✅

- [x] `IndexDrivenRuleParityTests`（Rule/Adapter/Merged vs walk 基线，≥ 99.9%）
- [x] `EngineRpcSharedIndexTests`（AC-B03：Recommend + Search 仅一次扫描）
- [x] `RulesNoDirectoryWalkTests`（AC-B01：`Rules/` 零 `Directory.Enumerate*`)
- [x] `IndexQueryPerformanceTests`（~100k 节点 smoke 门禁 < 100 ms）
- [ ] BenchmarkDotNet 4M 节点正式基准（CI nightly，待 VHDX 夹具）
- [x] security-review：索引合并未扩大删除面；删除仍仅经 `CleanupPlanner` + 隔离区
- [x] 更新 `05-architecture.md` M2.5 退出标准

---

## 风险登记

| 风险 | 缓解 |
|------|------|
| MFT 时间解析不准 | FileInfo 抽样；失败 → `DateTime.MinValue` |
| 首次 Clean 变慢 | USN 增量 + 进度 UI |
| v1 缓存失效 | 预期；Release note |
| Junction 行为 | 索引不跟随 reparse；parity fixture |

## 另请参阅

- [ADR-001 索引缓存 v2](../adr/ADR-001-index-cache-v2.md)
- [01-scan-engine.md](../specs/01-scan-engine.md) SCAN-FR-026..029
- [02-rules-engine.md](../specs/02-rules-engine.md) RULE-FR-026..027
