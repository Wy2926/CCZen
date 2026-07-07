using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;

namespace CCZen.Engine.Rules;

/// <summary>
/// Declarative application adapter manifest (spec: 03 ADPT-FR-001..003).
/// Adapters are data only: they declare how an app is discovered on this
/// machine (registry, path probe, process, configProbe) and which items are
/// cleanable once discovered. No code execution.
/// </summary>
public sealed class AdapterPack
{
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("adapters")]
    public required List<Adapter> Adapters { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Validates against the embedded schema and deserializes; throws on invalid packs.</summary>
    public static AdapterPack Load(string json)
    {
        JsonSchema schema = JsonSchema.FromText(Schema);
        EvaluationResults results = schema.Evaluate(
            JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!results.IsValid)
        {
            string details = string.Join("; ", (results.Details ?? [])
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Values.Select(e => $"{d.InstanceLocation}: {e}")));
            throw new InvalidDataException($"Adapter pack failed schema validation: {details}");
        }

        return JsonSerializer.Deserialize<AdapterPack>(json, SerializerOptions)
            ?? throw new InvalidDataException("Adapter pack deserialized to null.");
    }

    /// <summary>JSON Schema (draft 2020-12) for adapter packs (ADPT-FR-001).</summary>
    public const string Schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["schemaVersion", "adapters"],
          "properties": {
            "schemaVersion": { "type": "integer", "minimum": 1 },
            "adapters": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["id", "name", "category", "detect", "items"],
                "properties": {
                  "id": { "type": "string", "minLength": 1 },
                  "name": { "type": "string", "minLength": 1 },
                  "category": { "enum": ["im", "browser", "devtool", "game", "creative", "system", "other"] },
                  "detect": {
                    "type": "object",
                    "properties": {
                      "uninstallNamePatterns": { "type": "array", "items": { "type": "string" } },
                      "pathPatterns": { "type": "array", "items": { "type": "string" } },
                      "processNames": { "type": "array", "items": { "type": "string" } }
                    },
                    "additionalProperties": false
                  },
                  "items": {
                    "type": "array",
                    "minItems": 1,
                    "items": {
                      "type": "object",
                      "required": ["id", "tier", "targets", "explain"],
                      "properties": {
                        "id": { "type": "string", "minLength": 1 },
                        "tier": { "enum": ["T0", "T1", "T2", "T3"] },
                        "targets": { "type": "array", "minItems": 1, "items": { "type": "string" } },
                        "explain": { "type": "string", "minLength": 1 },
                        "requiresAppNotRunning": { "type": "boolean" }
                      },
                      "additionalProperties": false
                    }
                  }
                },
                "additionalProperties": false
              }
            }
          },
          "additionalProperties": false
        }
        """;
}

/// <summary>One application adapter (ADPT-FR-001).</summary>
public sealed class Adapter
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("detect")]
    public required AdapterDetect Detect { get; init; }

    [JsonPropertyName("items")]
    public required List<AdapterItem> Items { get; init; }
}

/// <summary>Multi-path discovery declaration (ADPT-FR-002).</summary>
public sealed class AdapterDetect
{
    /// <summary>Case-insensitive substrings matched against Uninstall display names (RULE-FR-002).</summary>
    [JsonPropertyName("uninstallNamePatterns")]
    public List<string>? UninstallNamePatterns { get; init; }

    /// <summary>Symbolized paths whose existence indicates the app is present.</summary>
    [JsonPropertyName("pathPatterns")]
    public List<string>? PathPatterns { get; init; }

    /// <summary>Lower-case process names (no extension) from the snapshot (RULE-FR-005).</summary>
    [JsonPropertyName("processNames")]
    public List<string>? ProcessNames { get; init; }
}

/// <summary>One cleanable item of a detected application (ADPT-FR-004).</summary>
public sealed class AdapterItem
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("tier")]
    public required string Tier { get; init; }

    /// <summary>Symbolized globs; a trailing "\*" enumerates children (e.g. per browser profile).</summary>
    [JsonPropertyName("targets")]
    public required List<string> Targets { get; init; }

    [JsonPropertyName("explain")]
    public required string Explain { get; init; }

    /// <summary>Precondition: only recommend when none of the adapter's processes are running (ADPT-FR-004).</summary>
    [JsonPropertyName("requiresAppNotRunning")]
    public bool RequiresAppNotRunning { get; init; }
}
