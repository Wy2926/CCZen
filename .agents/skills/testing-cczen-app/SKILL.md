---
name: testing-cczen-app
description: End-to-end GUI testing for the CCZen WinUI 3 app (recommend → one-click clean → undo). Use when verifying CCZen.App UI changes or the engine clean pipeline through the GUI.
---

# Testing the CCZen WinUI App

## Build & Launch
- Build: `dotnet build src\CCZen.App\CCZen.App.csproj -warnaserror -p:Platform=x64` (solution `dotnet build` also works; App is in the sln).
- Launch the exe directly: `src\CCZen.App\bin\x64\Debug\net8.0-windows10.0.19041.0\CCZen.App.exe` (unpackaged, `WindowsPackageType=None`).
- No service needed: `EngineClient` tries `\\.\pipe\cczen-engine` for ~1.5 s then falls back to an in-process `EngineRpcServer`. To test the RPC path instead, start `dotnet run --project src\CCZen.Service` first.

## Deterministic test data (sentinel files)
Seed files that baseline rules must pick up, so assertions don't depend on machine state:
- `%LOCALAPPDATA%\<AnyName>\Cache\sentinel.bin` (e.g. 30 MB) → hits `generic-app-cache` (T1).
- `%TEMP%\sentinel.log` → hits `system-user-temp` / `generic-log-dump-files` (T0/T1).
Use distinctive names and exact byte sizes so restore can be verified byte-for-byte.

## GUI flow & assertions
1. On launch, 一键清理 and 撤销上一批次 buttons must be disabled (CanExecute).
2. Click 扫描推荐 → summary "共 N 项推荐，可自动清理约 X"; sentinel path appears with expected tier; clean button enables.
3. Click 一键清理 → status "批次 <id>：k/n 项已移入隔离区". Verify on disk: original path gone, file present under `C:\CCZen.Quarantine\<batchId>\`. A few Skipped items (locked/fingerprint mismatch) are expected safe behavior, not bugs.
4. Click 撤销上一批次 → status "n 项已还原"; sentinel back at original path with identical byte count; undo button disables.

## Gotchas
- One-click clean quarantines ALL T0/T1 items on the machine — fine on a disposable VM (reversible via undo), but always undo or clean up `C:\CCZen.Quarantine` afterwards.
- Delete leftover quarantine batch dirs from earlier runs before asserting on batch listings.
- ListView is virtualized; scroll to find sentinel rows (T1 rows come after T0 rows; list sorted tier then size desc).
- Screen recording: maximize the window via its title-bar maximize button before starting.
- If WindowsAppSDK build fails with NETSDK1206 on .NET 8, the package version might be too old (1.5.x); 1.6+ fixed the versioned-RID assets.

## Devin Secrets Needed
None — everything runs locally on the Windows box.
