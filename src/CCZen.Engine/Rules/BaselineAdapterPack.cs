namespace CCZen.Engine.Rules;

/// <summary>
/// Built-in v1 adapter set (spec: 03 首发 Adapter 集): Chromium browsers
/// (Chrome/Edge per-profile caches), package-manager caches (npm/NuGet/pip),
/// and GPU shader caches. Every item's semantics are registered in the
/// feasibility matrix (docs/specs/06, A-xx).
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
