namespace CCZen.Engine.Rules;

/// <summary>
/// Built-in baseline rule pack shipped with the engine (spec: 02 规则包格式).
/// T0 categories map to Storage Sense / cleanmgr semantics; generic heuristics
/// cover the long tail (RULE-FR-010..023).
/// </summary>
public static class BaselineRulePack
{
    public static RulePack Load() => RulePack.Load(Json);

    public const string Json = """
        {
          "schemaVersion": 1,
          "lexicons": {
            "cache_words": [
              "cache", "caches", "tmp", "temp", "logs", "log", "gpucache",
              "shadercache", "shader_cache", "crashdumps", "crashpad", "dawncache",
              "code cache", "blob_storage", "service worker"
            ],
            "user_asset_exts": [
              ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf",
              ".jpg", ".jpeg", ".png", ".heic", ".raw", ".cr2", ".nef",
              ".mp4", ".mov", ".avi", ".mkv",
              ".psd", ".ai", ".sketch", ".fig",
              ".sln", ".csproj", ".vcxproj", ".key", ".pem", ".pst", ".ost"
            ]
          },
          "rules": [
            {
              "id": "system-user-temp",
              "tierCap": "T0",
              "targets": ["${TEMP}"],
              "signals": { "location": 1.0, "regenerable": 1.0 },
              "action": "delete-contents",
              "explain": "用户临时目录内容；Windows 存储感知清理同一位置（RULE-FR-010）"
            },
            {
              "id": "system-thumbnail-cache",
              "tierCap": "T0",
              "targets": ["${LOCALAPPDATA}/Microsoft/Windows/Explorer"],
              "match": { "fileExtensions": [".db"] },
              "signals": { "location": 1.0, "regenerable": 1.0 },
              "action": "recycle",
              "explain": "资源管理器缩略图缓存，删除后自动重建（RULE-FR-016）"
            },
            {
              "id": "system-wer-reports",
              "tierCap": "T0",
              "targets": ["${PROGRAMDATA}/Microsoft/Windows/WER"],
              "signals": { "location": 1.0, "regenerable": 0.9 },
              "action": "quarantine",
              "explain": "Windows 错误报告队列，与磁盘清理同类项（RULE-FR-015）"
            },
            {
              "id": "generic-app-cache",
              "tierCap": "T1",
              "targets": ["${LOCALAPPDATA}/**"],
              "match": { "dirNameLexicon": "cache_words", "excludeContentLexicon": "user_asset_exts" },
              "signals": { "location": 0.8, "regenerable": 0.7 },
              "action": "quarantine",
              "explain": "位于本地应用数据区的缓存形态目录，删除后应用会自动重建（RULE-FR-020）"
            },
            {
              "id": "generic-log-dump-files",
              "tierCap": "T1",
              "targets": ["${LOCALAPPDATA}/**", "${TEMP}/**", "${PROGRAMDATA}/**"],
              "match": { "fileExtensions": [".log", ".tmp", ".dmp", ".etl", ".old"] },
              "signals": { "location": 0.7, "regenerable": 0.8 },
              "action": "quarantine",
              "explain": "日志/转储形态文件（RULE-FR-021）"
            },
            {
              "id": "generic-bak-files",
              "tierCap": "T2",
              "targets": ["${LOCALAPPDATA}/**", "${TEMP}/**"],
              "match": { "fileExtensions": [".bak"] },
              "signals": { "location": 0.5, "regenerable": 0.4 },
              "action": "quarantine",
              "explain": "备份形态文件，风险档更高，逐项确认（RULE-FR-021）"
            },
            {
              "id": "generic-installer-leftover",
              "tierCap": "T2",
              "targets": ["${DOWNLOADS}", "${TEMP}"],
              "match": { "fileExtensions": [".exe", ".msi", ".msix"] },
              "signals": { "location": 0.4, "regenerable": 0.6 },
              "action": "recycle",
              "explain": "下载区安装介质；确认对应应用已安装后可回收（RULE-FR-022）"
            }
          ]
        }
        """;
}
