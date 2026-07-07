using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;

namespace CCZen.Engine.Rules;

/// <summary>
/// A signed data-only rule pack (spec: 02 规则包格式). Rules are pure data —
/// no code execution — and must pass JSON Schema validation before loading.
/// </summary>
public sealed class RulePack
{
    [JsonPropertyName("schemaVersion")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("lexicons")]
    public required Dictionary<string, string[]> Lexicons { get; init; }

    [JsonPropertyName("rules")]
    public required List<Rule> Rules { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Validates against the embedded schema and deserializes; throws on invalid packs.</summary>
    public static RulePack Load(string json)
    {
        JsonSchema schema = JsonSchema.FromText(Schema);
        EvaluationResults results = schema.Evaluate(
            JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!results.IsValid)
        {
            string details = string.Join("; ", (results.Details ?? [])
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Values.Select(e => $"{d.InstanceLocation}: {e}")));
            throw new InvalidDataException($"Rule pack failed schema validation: {details}");
        }

        return JsonSerializer.Deserialize<RulePack>(json, SerializerOptions)
            ?? throw new InvalidDataException("Rule pack deserialized to null.");
    }

    /// <summary>JSON Schema (draft 2020-12) for rule packs.</summary>
    public const string Schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "required": ["schemaVersion", "lexicons", "rules"],
          "properties": {
            "schemaVersion": { "type": "integer", "minimum": 1 },
            "lexicons": {
              "type": "object",
              "additionalProperties": { "type": "array", "items": { "type": "string" } }
            },
            "rules": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["id", "tierCap", "targets", "action", "explain"],
                "properties": {
                  "id": { "type": "string", "minLength": 1 },
                  "tierCap": { "enum": ["T0", "T1", "T2", "T3"] },
                  "targets": { "type": "array", "minItems": 1, "items": { "type": "string" } },
                  "match": {
                    "type": "object",
                    "properties": {
                      "dirNameLexicon": { "type": "string" },
                      "fileExtensions": { "type": "array", "items": { "type": "string" } },
                      "excludeContentLexicon": { "type": "string" }
                    },
                    "additionalProperties": false
                  },
                  "signals": {
                    "type": "object",
                    "additionalProperties": { "type": "number", "minimum": 0, "maximum": 1 }
                  },
                  "action": { "enum": ["quarantine", "recycle", "delete-contents", "report-only"] },
                  "explain": { "type": "string", "minLength": 1 }
                },
                "additionalProperties": false
              }
            }
          },
          "additionalProperties": false
        }
        """;
}

/// <summary>A single declarative cleanup rule (spec: 02 规则包格式).</summary>
public sealed class Rule
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Upper bound of the tier this rule may produce; evidence can only demote further.</summary>
    [JsonPropertyName("tierCap")]
    public required string TierCap { get; init; }

    /// <summary>Symbolized location globs, e.g. "${LOCALAPPDATA}/**".</summary>
    [JsonPropertyName("targets")]
    public required List<string> Targets { get; init; }

    [JsonPropertyName("match")]
    public RuleMatch? Match { get; init; }

    [JsonPropertyName("signals")]
    public Dictionary<string, double>? Signals { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("explain")]
    public required string Explain { get; init; }
}

/// <summary>Lexicon-driven matching constraints for a rule.</summary>
public sealed class RuleMatch
{
    /// <summary>Lexicon of directory-name words (e.g. cache/tmp/logs) — RULE-FR-020.</summary>
    [JsonPropertyName("dirNameLexicon")]
    public string? DirNameLexicon { get; init; }

    /// <summary>File extensions this rule matches (e.g. ".log", ".dmp") — RULE-FR-021.</summary>
    [JsonPropertyName("fileExtensions")]
    public List<string>? FileExtensions { get; init; }

    /// <summary>Lexicon of user-asset extensions that veto the candidate (content_type signal).</summary>
    [JsonPropertyName("excludeContentLexicon")]
    public string? ExcludeContentLexicon { get; init; }
}
