namespace Deduplicator.Data.Models;

public class ScanSession
{
    public int Id { get; set; }

    public int ContainerId { get; set; }

    public string RootPath { get; set; } = null!;

    public string Status { get; set; } = "in_progress"; // in_progress, completed, failed

    public long StartedAt { get; set; }

    public long? CompletedAt { get; set; }

    public int FilesProcessed { get; set; } = 0;

    public int? FilesTotal { get; set; }

    // Navigation properties
    public Container Container { get; set; } = null!;

    public ICollection<File> Files { get; set; } = new List<File>();
}
