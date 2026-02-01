namespace Deduplicator.Data.Models;

public class FileTask
{
    public int Id { get; set; }

    public int? FileId { get; set; }

    public string Operation { get; set; } = null!;

    public long? NewTimestamp { get; set; }

    // Navigation property
    public File? File { get; set; }
}
