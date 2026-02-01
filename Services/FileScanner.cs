using Microsoft.EntityFrameworkCore;
using Deduplicator.Data;
using Deduplicator.Data.Models;

namespace Deduplicator.Services;

public class FileScanner
{
    private readonly DeduplicatorContext _context;
    private readonly IContainerService _containerService;
    private readonly IMetadataReader _metadataReader;
    private readonly ProgressReporter _progressReporter;
    private const int BatchSize = 100;

    public FileScanner(
        DeduplicatorContext context,
        IContainerService containerService,
        IMetadataReader metadataReader,
        ProgressReporter progressReporter)
    {
        _context = context;
        _containerService = containerService;
        _metadataReader = metadataReader;
        _progressReporter = progressReporter;
    }

    public async Task<int> ScanDirectoryAsync(
        string directoryPath,
        int scanSessionId,
        bool resume = false,
        HashSet<string>? processedFiles = null)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");
        }

        // Normalize the directory path
        directoryPath = Path.GetFullPath(directoryPath);

        // Get container information
        var (partitionGuid, diskId) = await _containerService.GetContainerInfoAsync(directoryPath);
        var container = await GetOrCreateContainerAsync(partitionGuid, diskId);

        // Get the drive root (e.g., "D:\")
        var driveRoot = Path.GetPathRoot(directoryPath)!;

        // Discover all files
        _progressReporter.StartDiscovery();
        var allFiles = DiscoverFiles(directoryPath);

        // Filter out already processed files if resuming
        if (resume && processedFiles != null)
        {
            allFiles = allFiles.Where(f => !processedFiles.Contains(f)).ToList();
        }

        _progressReporter.ReportDiscovery(allFiles.Count);

        // Update session with total file count
        var session = await _context.ScanSessions.FindAsync(scanSessionId);
        if (session != null)
        {
            session.FilesTotal = allFiles.Count + (processedFiles?.Count ?? 0);
            await _context.SaveChangesAsync();
        }

        // Process files in batches
        var filesProcessed = processedFiles?.Count ?? 0;
        var batch = new List<Data.Models.File>();

        for (int i = 0; i < allFiles.Count; i++)
        {
            var filePath = allFiles[i];

            try
            {
                var fileInfo = new FileInfo(filePath);
                var relativePath = GetRelativePath(driveRoot, filePath);
                var fileName = fileInfo.Name;
                var directory = Path.GetDirectoryName(relativePath) ?? "";

                // Get media type
                var mediaType = _metadataReader.GetMediaType(filePath);

                // Read metadata
                var metadata = await _metadataReader.ReadMetadataAsync(filePath);

                // Get filesystem timestamps
                var filesystemCreationTime = new DateTimeOffset(fileInfo.CreationTimeUtc).ToUnixTimeSeconds();
                var filesystemModifiedTime = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds();

                // Extract timestamp from filename
                var filenameTimestamp = FilenameTimestampParser.ParseTimestamp(fileName);

                // Create or update file record
                var existingFile = await _context.Files
                    .FirstOrDefaultAsync(f =>
                        f.ContainerId == container.Id &&
                        f.Path == directory &&
                        f.Name == fileName);

                if (existingFile != null)
                {
                    // Update existing record
                    existingFile.MediaType = mediaType;
                    existingFile.Size = fileInfo.Length;
                    existingFile.MetadataTimestamp = metadata.MetadataTimestamp;
                    existingFile.FilesystemCreationTime = filesystemCreationTime;
                    existingFile.FilesystemModifiedTime = filesystemModifiedTime;
                    existingFile.MetadataMd5 = metadata.MetadataMd5;
                    existingFile.FilenameTimestamp = filenameTimestamp;
                    existingFile.LastScanSessionId = scanSessionId;
                }
                else
                {
                    // Create new record
                    var file = new Data.Models.File
                    {
                        ContainerId = container.Id,
                        Name = fileName,
                        Path = directory,
                        MediaType = mediaType,
                        Size = fileInfo.Length,
                        MetadataTimestamp = metadata.MetadataTimestamp,
                        FilesystemCreationTime = filesystemCreationTime,
                        FilesystemModifiedTime = filesystemModifiedTime,
                        MetadataMd5 = metadata.MetadataMd5,
                        FilenameTimestamp = filenameTimestamp,
                        LastScanSessionId = scanSessionId
                    };

                    _context.Files.Add(file);
                }

                filesProcessed++;

                // Save batch
                if (filesProcessed % BatchSize == 0)
                {
                    await _context.SaveChangesAsync();

                    // Update session progress
                    if (session != null)
                    {
                        session.FilesProcessed = filesProcessed;
                        await _context.SaveChangesAsync();
                    }
                }

                // Update progress
                _progressReporter.UpdateProgress(filesProcessed, allFiles.Count + (processedFiles?.Count ?? 0), filePath);
            }
            catch (Exception ex)
            {
                _progressReporter.ReportWarning($"Failed to process {filePath}: {ex.Message}");
            }
        }

        // Save any remaining files
        await _context.SaveChangesAsync();

        // Update final session progress
        if (session != null)
        {
            session.FilesProcessed = filesProcessed;
            await _context.SaveChangesAsync();
        }

        return filesProcessed;
    }

    private List<string> DiscoverFiles(string directoryPath)
    {
        var files = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                if (_metadataReader.IsSupportedFile(file))
                {
                    files.Add(file);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip directories we don't have access to
        }

        return files;
    }

    private async Task<Container> GetOrCreateContainerAsync(string? partitionGuid, string diskId)
    {
        var container = await _context.Containers
            .FirstOrDefaultAsync(c => c.PartitionGuid == partitionGuid && c.DiskId == diskId);

        if (container == null)
        {
            container = new Container
            {
                PartitionGuid = partitionGuid,
                DiskId = diskId
            };

            _context.Containers.Add(container);
            await _context.SaveChangesAsync();
        }

        return container;
    }

    private string GetRelativePath(string root, string fullPath)
    {
        var rootUri = new Uri(root);
        var fullUri = new Uri(fullPath);
        var relativeUri = rootUri.MakeRelativeUri(fullUri);
        return Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
    }
}
