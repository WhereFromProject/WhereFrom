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
    [property: JsonPropertyName("referrerUrl")] string? ReferrerUrl,
    [property: JsonPropertyName("completeness")] string Completeness,
    [property: JsonPropertyName("conflict")] bool Conflict,
    [property: JsonPropertyName("historicalBrowserRecords")] int HistoricalBrowserRecords,
    [property: JsonPropertyName("possibleBrowserRecords")] int PossibleBrowserRecords,
    [property: JsonPropertyName("browserStoreStatus")] string BrowserStoreStatus,
    [property: JsonPropertyName("evidence")] IReadOnlyList<ProvenanceEvidenceJson> Evidence)
{
    public static string Serialize(ProvenanceResult result) => Serialize(new ProvenanceReport(result, [], 0, null));

    public static string Serialize(ProvenanceReport report) => JsonSerializer.Serialize(
        new ProvenanceJson(
            1,
            report.Windows.Path,
            report.HasProvenance,
            report.Windows.Zone is { } zone ? new ProvenanceZoneJson(zone.Id, zone.Name) : null,
            report.SourceUrl,
            report.ReferrerUrl,
            report.Completeness,
            report.HasConflict,
            report.HistoricalBrowserRecords,
            report.PossibleBrowserDownloads.Count,
            report.BrowserError is null ? "ok" : "unavailable",
            BuildEvidence(report)));

    private static IReadOnlyList<ProvenanceEvidenceJson> BuildEvidence(ProvenanceReport report)
    {
        var evidence = new List<ProvenanceEvidenceJson>();
        var windows = report.Windows;
        if (windows.HasProvenance)
            evidence.Add(new("windows", null, windows.SourceUrl, null, null,
                windows.ReferrerUrl,
                windows.Zone is { } zone ? new ProvenanceZoneJson(zone.Id, zone.Name) : null,
                null, null, null, null));
        foreach (var download in report.BrowserDownloads)
            evidence.Add(new("browser", download.Browser,
                download.FinalUrl ?? download.InitialUrl,
                download.InitialUrl, download.FinalUrl, download.ReferrerUrl,
                null, download.StartedAt, download.CompletedAt, "consistent", download.MatchBasis));
        foreach (var download in report.PossibleBrowserDownloads)
            evidence.Add(new("browser", download.Browser,
                download.FinalUrl ?? download.InitialUrl,
                download.InitialUrl, download.FinalUrl, download.ReferrerUrl,
                null, download.StartedAt, download.CompletedAt, "possible", download.MatchBasis));
        return evidence;
    }
}

internal sealed record ProvenanceZoneJson(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record ProvenanceEvidenceJson(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("browser")] string? Browser,
    [property: JsonPropertyName("sourceUrl")] string? SourceUrl,
    [property: JsonPropertyName("initialUrl")] string? InitialUrl,
    [property: JsonPropertyName("finalUrl")] string? FinalUrl,
    [property: JsonPropertyName("referrerUrl")] string? ReferrerUrl,
    [property: JsonPropertyName("zone")] ProvenanceZoneJson? Zone,
    [property: JsonPropertyName("startedAt")] DateTimeOffset? StartedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("match")] string? Match,
    [property: JsonPropertyName("matchBasis")] string? MatchBasis);
