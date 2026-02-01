namespace Deduplicator.Data.Models;

public class File
{
    public int Id { get; set; }

    public int ContainerId { get; set; }

    public string Name { get; set; } = null!;

    public string Path { get; set; } = null!;

    public string MediaType { get; set; } = null!;

    public long Size { get; set; }

    public long? MetadataTimestamp { get; set; }

    public long? FilesystemCreationTime { get; set; }

    public long? FilesystemModifiedTime { get; set; }

    public required string MetadataMd5 { get; set; }

    public long? FilenameTimestamp { get; set; }

    public int? LastScanSessionId { get; set; }

    // Navigation properties
    public Container Container { get; set; } = null!;

    public ScanSession? LastScanSession { get; set; }
}
