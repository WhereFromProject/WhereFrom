namespace WhereFrom.Core;

public enum ProvenanceReadStatus
{
    Read,
    NoMetadata,
    FileNotFound,
    AccessDenied,
    ReadFailed,
    InvalidPath,
    NotAFile
}

public sealed record ProvenanceZone(int Id, string? Name);

public sealed record ProvenanceResult(string Path, string Provider, ProvenanceReadStatus Status)
{
    public ProvenanceZone? Zone { get; init; }
    public string? SourceUrl { get; init; }
    public string? ReferrerUrl { get; init; }
    public IReadOnlyList<ProvenanceIssue> Issues { get; init; } = [];
    public string? Error { get; init; }

    // Evidence availability is not a safety or trust assessment.
    public bool HasProvenance => Status == ProvenanceReadStatus.Read
        && (Zone is not null || SourceUrl is not null || ReferrerUrl is not null);
}
