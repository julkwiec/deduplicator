namespace Deduplicator.Services;

public record FileMetadata(
    long? MetadataTimestamp,
    string MetadataMd5
);

public interface IMetadataReader
{
    /// <summary>
    /// Reads metadata from a photo or video file
    /// </summary>
    Task<FileMetadata> ReadMetadataAsync(string filePath);

    /// <summary>
    /// Checks if the file extension is supported
    /// </summary>
    bool IsSupportedFile(string filePath);

    /// <summary>
    /// Gets the media type (picture or video) based on file extension
    /// </summary>
    string GetMediaType(string filePath);
}
