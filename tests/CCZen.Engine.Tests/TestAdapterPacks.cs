using CCZen.Engine.Rules;

namespace CCZen.Engine.Tests;

internal static class TestAdapterPacks
{
    internal static AdapterPack ChromeBrowser() => AdapterPack.Load("""
        {
          "schemaVersion": 1,
          "adapters": [
            {
              "id": "chrome",
              "name": "Chrome",
              "category": "browser",
              "detect": {
                "pathPatterns": ["${LOCALAPPDATA}\\Google\\Chrome\\User Data"],
                "processNames": ["chrome"]
              },
              "items": [
                {
                  "id": "http-cache",
                  "tier": "T1",
                  "targets": ["${LOCALAPPDATA}\\Google\\Chrome\\User Data\\*\\Cache"],
                  "explain": "per-profile cache",
                  "requiresAppNotRunning": true
                }
              ]
            }
          ]
        }
        """);
}
