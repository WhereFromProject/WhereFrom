using System.Text.Json;
using System.Text.Json.Serialization;
using WhereFrom.Core;

namespace WhereFrom.Cli;

// This DTO is the public JSON contract; domain properties are mapped explicitly.
internal sealed record ProvenanceJson(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("hasProvenance")] bool HasProvenance,
    [property: JsonPropertyName("zone")] ProvenanceZoneJson? Zone,
    [property: JsonPropertyName("sourceUrl")] string? SourceUrl,
    [property: JsonPropertyName("referrerUrl")] string? ReferrerUrl)
{
    public static string Serialize(ProvenanceResult result) => JsonSerializer.Serialize(
        new ProvenanceJson(
            1,
            result.Path,
            result.HasProvenance,
            result.Zone is { } zone ? new ProvenanceZoneJson(zone.Id, zone.Name) : null,
            result.SourceUrl,
            result.ReferrerUrl));
}

internal sealed record ProvenanceZoneJson(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);
