using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Deduplicator.Data;
using Deduplicator.Services;

namespace Deduplicator.Commands;

public class DeduplicateCommand
{
    public static async Task<int> ExecuteAsync(string dbPath)
    {
        try
        {
            // Initialize database
            using var context = new DeduplicatorContext(dbPath);
            context.EnsureCreated();

            AnsiConsole.MarkupLine("[cyan]Loading tasks...[/]");
            AnsiConsole.WriteLine();

            // Load all tasks with their associated files and containers
            var tasks = await context.FileTasks
                .Include(t => t.File)
                    .ThenInclude(f => f!.Container)
                .OrderBy(t => t.File!.Container.Id)
                .ToListAsync();

            if (tasks.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No tasks found in the database.[/]");
                AnsiConsole.MarkupLine("[yellow]Run 'prepare' command first to generate tasks.[/]");
                return 0;
            }

            // Group tasks by container
            var tasksByContainer = tasks
                .Where(t => t.File != null)
                .GroupBy(t => t.File!.Container)
                .ToList();

            AnsiConsole.MarkupLine($"[cyan]Found {tasks.Count} tasks across {tasksByContainer.Count} device(s)[/]");
            AnsiConsole.WriteLine();

            var containerService = new ContainerService();
            var totalProcessed = 0;
            var totalFailed = 0;

            // Process each container
            foreach (var containerGroup in tasksByContainer)
            {
                var container = containerGroup.Key;
                var containerTasks = containerGroup.ToList();

                AnsiConsole.MarkupLine($"[bold cyan]Device:[/] PartitionGuid={container.PartitionGuid ?? "null"}, DiskId={container.DiskId}");
                AnsiConsole.MarkupLine($"[dim]Tasks for this device: {containerTasks.Count}[/]");
                AnsiConsole.WriteLine();

                // Check if the device is currently connected
                var driveLetter = await FindConnectedDriveAsync(containerService, container);

                if (driveLetter == null)
                {
                    AnsiConsole.MarkupLine("[yellow]Device not currently connected.[/]");

                    if (!AnsiConsole.Confirm($"Connect the device and press Enter to continue, or skip this device?", true))
                    {
                        AnsiConsole.MarkupLine("[dim]Skipping device...[/]");
                        AnsiConsole.WriteLine();
                        continue;
                    }

                    // Try again to find the drive
                    driveLetter = await FindConnectedDriveAsync(containerService, container);

                    if (driveLetter == null)
                    {
                        AnsiConsole.MarkupLine("[red]Device still not found. Skipping...[/]");
                        AnsiConsole.WriteLine();
                        continue;
                    }
                }

                AnsiConsole.MarkupLine($"[green]Device found at drive {driveLetter}:[/]");
                AnsiConsole.WriteLine();

                // Execute tasks for this container
                var processed = 0;
                var failed = 0;

                await AnsiConsole.Progress()
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask($"[cyan]Processing tasks for {driveLetter}:[/]", maxValue: containerTasks.Count);

                        foreach (var fileTask in containerTasks)
                        {
                            try
                            {
                                var fullPath = Path.Combine(driveLetter + ":\\", fileTask.File!.Path, fileTask.File.Name);

                                await ExecuteTaskAsync(context, fileTask, fullPath);
                                processed++;
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                AnsiConsole.MarkupLine($"[red]Failed to process task {fileTask.Id}: {ex.Message}[/]");
                            }

                            task.Increment(1);
                        }
                    });

                totalProcessed += processed;
                totalFailed += failed;

                AnsiConsole.MarkupLine($"[green]Completed: {processed} tasks processed, {failed} failed[/]");
                AnsiConsole.WriteLine();
            }

            // Display final summary
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Metric[/]");
            table.AddColumn("[cyan]Value[/]");

            table.AddRow("Total tasks processed", totalProcessed.ToString("N0"));
            table.AddRow("Total tasks failed", totalFailed.ToString("N0"));

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[green]Deduplication complete![/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static async Task<char?> FindConnectedDriveAsync(ContainerService containerService, Data.Models.Container container)
    {
        // Get all available drive letters
        var drives = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
            .ToList();

        foreach (var drive in drives)
        {
            try
            {
                var driveLetter = drive.Name.TrimEnd('\\', ':')[0];
                var (partitionGuid, diskId) = await containerService.GetContainerInfoAsync(drive.Name);

                // Match by both PartitionGuid and DiskId
                if (partitionGuid == container.PartitionGuid && diskId == container.DiskId)
                {
                    return driveLetter;
                }
            }
            catch
            {
                // Skip drives we can't access
                continue;
            }
        }

        return null;
    }

    private static async Task ExecuteTaskAsync(DeduplicatorContext context, Data.Models.FileTask fileTask, string fullPath)
    {
        // Use a transaction to ensure atomicity
        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            if (fileTask.Operation == "adjust")
            {
                await ExecuteAdjustTaskAsync(fileTask, fullPath);
            }
            else if (fileTask.Operation == "delete")
            {
                await ExecuteDeleteTaskAsync(fileTask, fullPath);
            }
            else
            {
                throw new InvalidOperationException($"Unknown operation: {fileTask.Operation}");
            }

            // Remove the task from the database
            context.FileTasks.Remove(fileTask);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task ExecuteAdjustTaskAsync(Data.Models.FileTask fileTask, string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {fullPath}");
        }

        if (!fileTask.NewTimestamp.HasValue)
        {
            throw new InvalidOperationException("Adjust task requires a valid NewTimestamp");
        }

        // Convert Unix timestamp to DateTime
        var newDateTime = DateTimeOffset.FromUnixTimeSeconds(fileTask.NewTimestamp.Value).UtcDateTime;

        // Set filesystem timestamps
        await Task.Run(() =>
        {
            File.SetCreationTimeUtc(fullPath, newDateTime);
            File.SetLastWriteTimeUtc(fullPath, newDateTime);
        });

        // Rename file to include timestamp if it doesn't already have one
        var extension = Path.GetExtension(fullPath).ToLower();
        var directory = Path.GetDirectoryName(fullPath)!;
        var mediaTypeSegment = fileTask.File!.MediaType switch
        {
            "picture" => "IMG",
            "video" => "VID",
            _ => throw new Exception($"Unsupported media type: {fileTask.File.MediaType}"),
        };
        var timestampSegment = newDateTime.ToString("yyyyMMdd_HHmmss");
        string newFullPath;
        var counter = 1;
        do
        {
            var newFileName = $"{mediaTypeSegment}_{timestampSegment}_{fileTask.File.MetadataMd5.ToUpper()}_{counter}{extension}";
            newFullPath = Path.Combine(directory, newFileName);
            counter++;
        }
        while (File.Exists(newFullPath));


        await Task.Run(() => File.Move(fullPath, newFullPath));
    }

    private static async Task ExecuteDeleteTaskAsync(Data.Models.FileTask fileTask, string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            // File already deleted or doesn't exist - consider this a success
            return;
        }

        // Delete the file
        await Task.Run(() => File.Delete(fullPath));
    }
}
