namespace WhereFrom.Core;

public enum ProvenanceField
{
    Metadata,
    Zone,
    SourceUrl,
    ReferrerUrl
}

public enum ProvenanceIssueKind
{
    EmptyValue,
    InvalidValue,
    DuplicateField,
    InvalidFormat,
    MissingSection
}

public sealed record ProvenanceIssue(ProvenanceField Field, ProvenanceIssueKind Kind);
