---
name: testing-cczen-app
description: GUI end-to-end testing of the CCZen WinUI app (scan/clean/undo, recommendation grouping, large-file conditional search). Use when verifying CCZen.App UI or engine behavior changes on a Windows box.
---

# Testing CCZen App (WinUI 3)

## Build & Launch

- Build from repo root: `dotnet build -warnaserror` (all 5 projects must be 0 warnings).
- Run the app directly (no packaging needed): `src\CCZen.App\bin\x64\Debug\net8.0-windows10.0.19041.0\CCZen.App.exe` via `Start-Process`.
- The GUI uses the in-process engine fallback when the named-pipe service (CCZen.Service) isn't running — that's the default test path. RPC path needs the service started separately if you want to cover it.
- Maximize the window before recording (click the maximize titlebar button).

## Sentinel Files (create BEFORE scanning/searching — the index is built at scan time)

- Recommendation (generic-app-cache, T1): 30 MB `%LOCALAPPDATA%\CCZenTestApp\Cache\cczen-sentinel.bin`.
- Search hit (deterministic): a large uniquely-named file, e.g. 300 MB `%TEMP%\cczen-big-sentinel.tmp` — search "仅文件 / ≥100MB / .tmp" must return exactly it; raising min size above its size must exclude it (counter-check).
- Sentinels may be wiped between sessions (temp cleanup / earlier clean runs) — always recreate with `[IO.File]::WriteAllBytes`.

## Golden-Path Tests

1. **Grouped recommendations**: 「清理推荐」→「扫描推荐」. Expect grouped Expander rows (Tier + RuleId + "N 项 · size"); summary shows "共 X 项推荐（Y 组）" with Y < X. `system-thumbnail-cache` reliably yields many hits on any Windows box — good multi-item group. Expand → detail rows sorted by size desc.
2. **Conditional file search**: 「大文件搜索」tab. First search auto-scans C:\ (status "首次搜索：正在扫描 C:\ …", ~seconds). Verify size + name filters with sentinel, then counter-check with a higher threshold.
3. **Ancestor-chain collapse**: kind=仅目录, min size ~500MB, empty name. Assert results contain NO `C:\`, `C:\Users`, `...\AppData`-style dominated ancestors — only "carrier" directories (WinSxS, AppData\Local, Temp, etc.).
4. Regression (M4-1): scan → 一键清理 → 撤销 with the 30 MB cache sentinel (see prior report on PR #14).

## Pitfalls

- **Stale TextBox value**: WinUI `x:Bind TwoWay` on TextBox may commit only on LostFocus — clicking 搜索 immediately after typing can run with the OLD value. Click elsewhere first (or search twice) before asserting on results. If this bug is fixed later, the workaround is harmless.
- ComboBox dropdown clicks can silently miss; screenshot after opening the dropdown and click the item by its actual position.
- CLI cross-check: `dotnet run --project src\CCZen.Cli --no-build -- recommend` (or `top`) is a fast way to predict what the GUI should show before recording.
- The results ListView scrolls — scroll to the bottom to assert absence of items (e.g. ancestor chains) over the FULL result set, not just the visible page.

## Devin Secrets Needed

- None — everything runs locally on the Windows test VM.
