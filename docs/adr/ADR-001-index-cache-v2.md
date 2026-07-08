---
title: ADR-001 — 索引落盘缓存格式 v2（追加 LastWriteTime）
ms.date: 2026-07-08
status: Accepted
deciders: CCZen 引擎组
---

# ADR-001 — 索引落盘缓存格式 v2

## 背景

方案 B（索引统一合并）要求规则引擎从 `FileSystemIndex` 读取 `staleness`（`RULE-FR-026`）与 `content_type` 信号，索引必须持久化每节点的 `LastWriteTimeUtc`（`SCAN-FR-026`）。当前 v1 缓存仅含 FRN 树、尺寸与 USN 水位。

## 决策

1. **Bump 缓存 schema 至 v2**：在 v1 二进制布局末尾追加 `long[] lastWriteUtc`（每节点 8 字节，UTC FileTime 或 0 表示未知）。
2. **校验和覆盖全文件**：`XxHash64` 计算范围含 v2 新字段（与 v1 行为一致，扩展 payload）。
3. **不兼容即重建**：`Load` 见 magic/version ≠ 2 → 返回 `null` → 触发全量 USN 扫描（`SCAN-FR-033`）。
4. **不在 v2 做向后写入 v1**：仅向前兼容读取；旧客户端读 v2 文件同样失败并重建。

## v2 文件布局（逻辑）

| 偏移 | 字段 | 类型 | 说明 |
|------|------|------|------|
| 0 | Magic | `uint32` | `0x58445A43`（"CZDX"） |
| 4 | Version | `uint16` | `2` |
| 6 | Header | … | 同 v1：rootFrn, UsnJournalId, NextUsn, nodeCount |
| … | Nodes | SoA | parent, name pool, isDirectory, logical, allocated |
| … | **LastWrite** | `int64[nodeCount]` | **v2 新增** |
| EOF-8 | Checksum | `uint64` | XxHash64(0 .. EOF-8) |

## 后果

### 正面

- 规则引擎可离线计算 staleness，无需 `FileInfo` I/O。
- 索引一次构建，清理与大文件搜索共享同一份缓存。

### 负面

- 升级后首次启动需全量重建（v1 `.idx` 作废）；NVMe 上通常 < 45 s（SCAN-NFR-001）。
- 内存 +8 B/节点（400 万文件 ≈ +32 MB），仍在 ARCH-NFR-001 预算内。

## 备选方案（已否决）

| 方案 | 否决理由 |
|------|----------|
| 规则评估时按需 `FileInfo` | 无法消除重复 I/O，违背方案 B 目标 |
| MemoryPack / 第三方序列化 | D10 开放问题倾向自定义二进制 + 校验和；引入依赖增加攻击面 |
| v1 缓存 lazy 补 mtime | 首次 Recommend 仍对百万文件做 stat，体验差 |

## 参考

- `SCAN-FR-026`、`SCAN-FR-029`、`SCAN-FR-032`、`SCAN-FR-033`
- 06 追溯表 F-13（MFT `$STANDARD_INFORMATION`）
- [Master File Table](https://learn.microsoft.com/windows/win32/fileio/master-file-table)
