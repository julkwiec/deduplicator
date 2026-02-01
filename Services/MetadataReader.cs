using System.Security.Cryptography;
using System.Text;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using FFMpegCore;
using System.Buffers;

namespace Deduplicator.Services;

public class MetadataReader : IMetadataReader
{
    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".heic", ".heif", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".flv", ".m4v", ".mpg", ".mpeg", ".3gp", ".webm"
    };

    public bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return PhotoExtensions.Contains(extension) || VideoExtensions.Contains(extension);
    }

    public string GetMediaType(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        if (PhotoExtensions.Contains(extension))
            return "picture";

        if (VideoExtensions.Contains(extension))
            return "video";

        throw new ArgumentException($"Unsupported file extension: {extension}", nameof(filePath));
    }

    public async Task<FileMetadata> ReadMetadataAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        if (PhotoExtensions.Contains(extension))
        {
            return await ReadPhotoMetadataAsync(filePath);
        }
        if (VideoExtensions.Contains(extension))
        {
            return await ReadVideoMetadataAsync(filePath);
        }
        return new FileMetadata(null, await GetMd5FromFileHeader(filePath));
    }

    private async Task<FileMetadata> ReadPhotoMetadataAsync(string filePath)
    {
        try
        {
            var directories = await Task.Run(() => ImageMetadataReader.ReadMetadata(filePath));

            long? timestamp = null;
            var metadataBuilder = new StringBuilder();

            // Extract timestamp
            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (exifSubIfd?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime) == true)
            {
                timestamp = new DateTimeOffset(dateTime).ToUnixTimeSeconds();
            }
            else if (exifSubIfd?.TryGetDateTime(ExifDirectoryBase.TagDateTime, out dateTime) == true)
            {
                timestamp = new DateTimeOffset(dateTime).ToUnixTimeSeconds();
            }

            // Build metadata blob for MD5 (excluding filesystem-specific directories)
            var excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "File",           // Contains filename, file size, file modified date
                "File Type"       // May contain path information
            };

            foreach (var directory in directories)
            {
                // Skip filesystem-specific directories
                if (excludedDirectories.Contains(directory.Name))
                    continue;

                foreach (var tag in directory.Tags)
                {
                    metadataBuilder.AppendLine($"{directory.Name}:{tag.Name}={tag.Description}");
                }
            }

            var metadataMd5 = ComputeMd5(metadataBuilder.ToString());

            return new FileMetadata(timestamp, metadataMd5);
        }
        catch (Exception)
        {
            // Return null values if metadata cannot be read
            return new FileMetadata(null, await GetMd5FromFileHeader(filePath));
        }
    }

    private async Task<FileMetadata> ReadVideoMetadataAsync(string filePath)
    {
        try
        {
            var mediaInfo = await FFProbe.AnalyseAsync(filePath);

            long? timestamp = null;

            // Extract timestamp from creation time
            if (mediaInfo.Format.Tags?.TryGetValue("creation_time", out var creationTime) == true)
            {
                if (DateTime.TryParse(creationTime, out var dateTime))
                {
                    timestamp = new DateTimeOffset(dateTime).ToUnixTimeSeconds();
                }
            }

            // Build metadata blob for MD5 (excluding filesystem-specific tags)
            var metadataBuilder = new StringBuilder();
            metadataBuilder.AppendLine($"Duration={mediaInfo.Duration}");
            metadataBuilder.AppendLine($"Format={mediaInfo.Format.FormatName}");

            var excludedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "filename",
                "file",
                "filepath",
                "file_path",
                "file_name"
            };

            if (mediaInfo.Format.Tags != null)
            {
                foreach (var tag in mediaInfo.Format.Tags)
                {
                    // Skip filesystem-specific tags
                    if (excludedTags.Contains(tag.Key))
                        continue;

                    metadataBuilder.AppendLine($"{tag.Key}={tag.Value}");
                }
            }

            foreach (var stream in mediaInfo.VideoStreams)
            {
                metadataBuilder.AppendLine($"VideoStream={stream.CodecName},{stream.Width}x{stream.Height},{stream.FrameRate}");
            }

            var metadataMd5 = ComputeMd5(metadataBuilder.ToString());

            return new FileMetadata(timestamp, metadataMd5);
        }
        catch (Exception)
        {
            // Return null values if metadata cannot be read
            return new FileMetadata(null, await GetMd5FromFileHeader(filePath));
        }
    }

    private string ComputeMd5(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string ComputeMd5(byte[] buffer, int count)
    {
        var hash = MD5.HashData(new ReadOnlySpan<byte>(buffer, 0, count));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string> GetMd5FromFileHeader(string filePath)
    {
        await using var fileStream = File.OpenRead(filePath);
        byte[]? tmpArray = null;
        try
        {
            tmpArray = ArrayPool<byte>.Shared.Rent(128 * 1024); // 128KB
            var fileHeader = new byte[128 * 1024];
            var bytesRead = await fileStream.ReadAsync(fileHeader);
            return ComputeMd5(fileHeader, bytesRead);
        }
        finally
        {
            if (tmpArray != null)
            {
                ArrayPool<byte>.Shared.Return(tmpArray);
            }
        }
    }
}
