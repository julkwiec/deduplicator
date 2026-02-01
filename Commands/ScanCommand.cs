using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Deduplicator.Data;
using Deduplicator.Services;

namespace Deduplicator.Commands;

public class ScanCommand
{
    public static async Task<int> ExecuteAsync(string directory, string dbPath, bool forceRestart = false)
    {
        try
        {
            // Normalize directory path
            directory = Path.GetFullPath(directory);

            if (!Directory.Exists(directory))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Directory not found: {directory}");
                return 1;
            }

            // Initialize database
            using var context = new DeduplicatorContext(dbPath);
            context.EnsureCreated();

            // Initialize services
            var containerService = new ContainerService();
            var metadataReader = new MetadataReader();
            var progressReporter = new ProgressReporter();
            var sessionManager = new ScanSessionManager(context);
            var fileScanner = new FileScanner(context, containerService, metadataReader, progressReporter);

            // Get container information
            var (partitionGuid, diskId) = await containerService.GetContainerInfoAsync(directory);
            var container = await context.Containers
                .FirstOrDefaultAsync(c => c.PartitionGuid == partitionGuid && c.DiskId == diskId);

            if (container == null)
            {
                container = new Data.Models.Container
                {
                    PartitionGuid = partitionGuid,
                    DiskId = diskId
                };
                context.Containers.Add(container);
                await context.SaveChangesAsync();
            }

            // Check for incomplete scan
            var incompleteSession = await sessionManager.FindIncompleteScanAsync(container.Id, directory);
            bool resume = false;
            HashSet<string>? processedFiles = null;

            if (incompleteSession != null && !forceRestart)
            {
                // Ask user about resuming
                var completedDate = DateTimeOffset.FromUnixTimeSeconds(incompleteSession.StartedAt).LocalDateTime;
                AnsiConsole.MarkupLine($"[yellow]Found incomplete scan session from {completedDate}[/]");
                AnsiConsole.MarkupLine($"  Progress: {incompleteSession.FilesProcessed} of ~{incompleteSession.FilesTotal ?? 0} files");
                AnsiConsole.WriteLine();

                resume = AnsiConsole.Confirm("Resume this scan?", true);

                if (resume)
                {
                    AnsiConsole.MarkupLine($"[green]Resuming scan session #{incompleteSession.Id}...[/]");
                    processedFiles = await sessionManager.GetProcessedFilesAsync(incompleteSession.Id);
                }
                else
                {
                    // Mark old session as failed and start new one
                    await sessionManager.FailScanAsync(incompleteSession.Id);
                    incompleteSession = null;
                }
            }

            // Create or use existing session
            var session = incompleteSession ?? await sessionManager.CreateScanSessionAsync(container.Id, directory);

            try
            {
                // Scan directory
                var filesProcessed = await fileScanner.ScanDirectoryAsync(
                    directory,
                    session.Id,
                    resume,
                    processedFiles);

                // Cleanup orphaned files
                await sessionManager.CleanupOrphanedFilesAsync(session.Id, container.Id, directory);

                // Mark session as completed
                await sessionManager.CompleteScanAsync(session.Id);

                progressReporter.Complete(filesProcessed);

                return 0;
            }
            catch (Exception ex)
            {
                await sessionManager.FailScanAsync(session.Id);
                progressReporter.ReportError($"Scan failed: {ex.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Fatal error:[/] {ex}");
            return 1;
        }
    }
}
