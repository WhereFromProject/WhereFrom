namespace WhereFrom.Core;

public interface IProvenanceProvider
{
    ProvenanceResult Inspect(string path);
}
