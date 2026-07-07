namespace CCZen.Engine.Rules;

/// <summary>
/// Built-in v1 adapter set (spec: 03 首发 Adapter 集): Chromium browsers
/// (Chrome/Edge per-profile caches), IM (WeChat with configProbe for migrated
/// data dirs, Telegram), package-manager caches (npm/NuGet/pip/Gradle/Maven),
/// Steam shader caches, and GPU shader caches. Every item's semantics are
/// registered in the feasibility matrix (docs/specs/06, A-xx).
/// </summary>
public static class BaselineAdapterPack
{
    public static AdapterPack Load() => AdapterPack.Load(Json);

    public const string Json = """
        {
          "schemaVersion": 1,
          "adapters": [
            {
              "id": "chrome",
              "name": "Google Chrome",
              "category": "browser",
              "detect": {
                "pathPatterns": ["${LOCALAPPDATA}\\Google\\Chrome\\User Data"],
                "processNames": ["chrome"]
              },
              "items": [
                {
                  "id": "http-cache",
                  "tier": "T1",
                  "targets": [
                    "${LOCALAPPDATA}\\Google\\Chrome\\User Data\\*\\Cache",
                    "${LOCALAPPDATA}\\Google\\Chrome\\User Data\\*\\Code Cache",
                    "${LOCALAPPDATA}\\Google\\Chrome\\User Data\\*\\GPUCache",
                    "${LOCALAPPDATA}\\Google\\Chrome\\User Data\\*\\Service Worker\\CacheStorage"
                  ],
                  "explain": "Chromium 按 profile 的 HTTP/代码/GPU/SW 缓存，重启后自动重建",
                  "requiresAppNotRunning": true
                },
                {
                  "id": "shader-cache",
                  "tier": "T1",
                  "targets": ["${LOCALAPPDATA}\\Google\\Chrome\\User Data\\ShaderCache"],
                  "explain": "Chromium 着色器缓存，自动重建",
                  "requiresAppNotRunning": true
                }
              ]
            },
            {
              "id": "edge",
              "name": "Microsoft Edge",
              "category": "browser",
              "detect": {
                "pathPatterns": ["${LOCALAPPDATA}\\Microsoft\\Edge\\User Data"],
                "processNames": ["msedge"]
              },
              "items": [
                {
                  "id": "http-cache",
                  "tier": "T1",
                  "targets": [
                    "${LOCALAPPDATA}\\Microsoft\\Edge\\User Data\\*\\Cache",
                    "${LOCALAPPDATA}\\Microsoft\\Edge\\User Data\\*\\Code Cache",
                    "${LOCALAPPDATA}\\Microsoft\\Edge\\User Data\\*\\GPUCache",
                    "${LOCALAPPDATA}\\Microsoft\\Edge\\User Data\\*\\Service Worker\\CacheStorage"
                  ],
                  "explain": "Chromium 按 profile 的 HTTP/代码/GPU/SW 缓存，重启后自动重建",
                  "requiresAppNotRunning": true
                }
              ]
            },
            {
              "id": "npm",
              "name": "npm",
              "category": "devtool",
              "detect": {
                "pathPatterns": ["${LOCALAPPDATA}\\npm-cache", "${APPDATA}\\npm-cache"]
              },
              "items": [
                {
                  "id": "cache",
                  "tier": "T1",
                  "targets": ["${LOCALAPPDATA}\\npm-cache", "${APPDATA}\\npm-cache"],
                  "explain": "npm 全局缓存（官方 npm cache clean 语义），按需重新下载"
                }
              ]
            },
            {
              "id": "nuget",
              "name": "NuGet",
              "category": "devtool",
              "detect": {
                "pathPatterns": ["${USERPROFILE}\\.nuget\\packages"]
              },
              "items": [
                {
                  "id": "global-packages",
                  "tier": "T2",
                  "targets": ["${USERPROFILE}\\.nuget\\packages"],
                  "explain": "NuGet 全局包缓存（官方 dotnet nuget locals 语义），删除后 restore 重新下载"
                },
                {
                  "id": "http-cache",
                  "tier": "T1",
                  "targets": ["${LOCALAPPDATA}\\NuGet\\v3-cache"],
                  "explain": "NuGet HTTP 缓存（官方 dotnet nuget locals http-cache 语义）"
                }
              ]
            },
            {
              "id": "pip",
              "name": "pip",
              "category": "devtool",
              "detect": {
                "pathPatterns": ["${LOCALAPPDATA}\\pip\\cache"]
              },
              "items": [
                {
                  "id": "cache",
                  "tier": "T1",
                  "targets": ["${LOCALAPPDATA}\\pip\\cache"],
                  "explain": "pip 下载缓存（官方 pip cache purge 语义）"
                }
              ]
            },
            {
              "id": "wechat",
              "name": "微信",
              "category": "im",
              "detect": {
                "uninstallNamePatterns": ["微信", "WeChat"],
                "pathPatterns": ["${DOCUMENTS}\\WeChat Files"],
                "processNames": ["wechat", "weixin"],
                "configProbes": [
                  {
                    "symbol": "WECHAT_FILES",
                    "kind": "registryValue",
                    "source": "HKCU\\Software\\Tencent\\WeChat",
                    "valueName": "FileSavePath"
                  }
                ]
              },
              "items": [
                {
                  "id": "file-cache",
                  "tier": "T1",
                  "targets": [
                    "${WECHAT_FILES}\\WeChat Files\\*\\FileStorage\\Cache",
                    "${DOCUMENTS}\\WeChat Files\\*\\FileStorage\\Cache"
                  ],
                  "explain": "微信按账号的临时文件缓存（FileStorage\\Cache），不含聊天记录",
                  "requiresAppNotRunning": true
                },
                {
                  "id": "chat-media",
                  "tier": "T2",
                  "targets": [
                    "${WECHAT_FILES}\\WeChat Files\\*\\FileStorage\\Image",
                    "${WECHAT_FILES}\\WeChat Files\\*\\FileStorage\\Video",
                    "${DOCUMENTS}\\WeChat Files\\*\\FileStorage\\Image",
                    "${DOCUMENTS}\\WeChat Files\\*\\FileStorage\\Video"
                  ],
                  "explain": "微信聊天图片/视频缓存——删除后聊天记录内无法查看原图/视频",
                  "requiresAppNotRunning": true
                }
              ]
            },
            {
              "id": "telegram",
              "name": "Telegram Desktop",
              "category": "im",
              "detect": {
                "uninstallNamePatterns": ["Telegram Desktop"],
                "pathPatterns": ["${APPDATA}\\Telegram Desktop\\tdata"],
                "processNames": ["telegram"]
              },
              "items": [
                {
                  "id": "media-cache",
                  "tier": "T1",
                  "targets": ["${APPDATA}\\Telegram Desktop\\tdata\\user_data"],
                  "explain": "Telegram 媒体缓存（应用内也提供官方清理入口），按需重新下载",
                  "requiresAppNotRunning": true
                }
              ]
            },
            {
              "id": "gradle",
              "name": "Gradle",
              "category": "devtool",
              "detect": {
                "pathPatterns": ["${USERPROFILE}\\.gradle\\caches"]
              },
              "items": [
                {
                  "id": "modules-cache",
                  "tier": "T1",
                  "targets": ["${USERPROFILE}\\.gradle\\caches\\modules-2"],
                  "explain": "Gradle 依赖模块缓存（官方文档记载可安全删除），构建时重新下载"
                },
                {
                  "id": "wrapper-dists",
                  "tier": "T2",
                  "targets": ["${USERPROFILE}\\.gradle\\wrapper\\dists"],
                  "explain": "Gradle wrapper 发行版缓存（含旧版本），删除后首次构建重新下载"
                }
              ]
            },
            {
              "id": "maven",
              "name": "Maven",
              "category": "devtool",
              "detect": {
                "pathPatterns": ["${USERPROFILE}\\.m2\\repository"]
              },
              "items": [
                {
                  "id": "local-repository",
                  "tier": "T3",
                  "targets": ["${USERPROFILE}\\.m2\\repository"],
                  "explain": "Maven 本地仓库——仅报告占用，不建议自动清理（specs/03：仅报告）"
                }
              ]
            },
            {
              "id": "steam",
              "name": "Steam",
              "category": "game",
              "detect": {
                "uninstallNamePatterns": ["Steam"],
                "pathPatterns": ["${PROGRAMFILES_X86}\\Steam\\steamapps"],
                "processNames": ["steam"]
              },
              "items": [
                {
                  "id": "shader-cache",
                  "tier": "T1",
                  "targets": ["${PROGRAMFILES_X86}\\Steam\\steamapps\\shadercache"],
                  "explain": "Steam 按游戏的预编译着色器缓存，运行游戏时自动重建",
                  "requiresAppNotRunning": true
                },
                {
                  "id": "download-leftovers",
                  "tier": "T1",
                  "targets": ["${PROGRAMFILES_X86}\\Steam\\steamapps\\downloading"],
                  "explain": "Steam 下载中断残留（downloading 目录），继续下载时重新获取",
                  "requiresAppNotRunning": true
                }
              ]
            },
            {
              "id": "gpu-shader-cache",
              "name": "GPU 着色器缓存",
              "category": "system",
              "detect": {
                "pathPatterns": [
                  "${LOCALAPPDATA}\\D3DSCache",
                  "${LOCALAPPDATA}\\NVIDIA\\DXCache",
                  "${LOCALAPPDATA}\\NVIDIA\\GLCache",
                  "${LOCALAPPDATA}\\AMD\\DxCache"
                ]
              },
              "items": [
                {
                  "id": "dx-cache",
                  "tier": "T1",
                  "targets": [
                    "${LOCALAPPDATA}\\D3DSCache",
                    "${LOCALAPPDATA}\\NVIDIA\\DXCache",
                    "${LOCALAPPDATA}\\NVIDIA\\GLCache",
                    "${LOCALAPPDATA}\\AMD\\DxCache"
                  ],
                  "explain": "显卡驱动着色器缓存，首次运行游戏/应用时自动重建"
                }
              ]
            }
          ]
        }
        """;
}
