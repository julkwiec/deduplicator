namespace Deduplicator.Data.Models;

public class Container
{
    public int Id { get; set; }

    public string? PartitionGuid { get; set; }

    public string DiskId { get; set; } = null!;

    // Navigation property
    public ICollection<File> Files { get; set; } = new List<File>();

    public ICollection<ScanSession> ScanSessions { get; set; } = new List<ScanSession>();
}
