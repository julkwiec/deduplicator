using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Deduplicator.Data;

namespace Deduplicator.Commands;

public class SummaryCommand
{
    public static async Task<int> ExecuteAsync(string dbPath)
    {
        try
        {
            if (!System.IO.File.Exists(dbPath))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Database file not found: {dbPath}");
                AnsiConsole.MarkupLine("[yellow]Hint:[/] Run a scan first to create the database.");
                return 1;
            }

            // Initialize database
            using var context = new DeduplicatorContext(dbPath);

            // Get total file count
            var totalFiles = await context.Files.CountAsync();

            if (totalFiles == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No files found in database.[/]");
                AnsiConsole.MarkupLine("[yellow]Run a scan first to populate the database.[/]");
                return 0;
            }

            // Find duplicates based on size + metadata_timestamp
            // Only consider files where metadata_timestamp is non-null
            var duplicateGroups = await context.Files
                .Where(f => f.MetadataTimestamp != null)
                .GroupBy(f => new { f.Size, f.MetadataTimestamp })
                .Where(g => g.Count() > 1)
                .Select(g => new
                {
                    Key = g.Key,
                    Count = g.Count(),
                    TotalSize = g.Sum(f => f.Size)
                })
                .ToListAsync();

            // Calculate statistics
            var uniqueGroups = duplicateGroups.Count;
            var totalDuplicates = duplicateGroups.Sum(g => g.Count);
            var uniqueFileSize = duplicateGroups.Sum(g => g.Key.Size);
            var totalDuplicateSize = duplicateGroups.Sum(g => g.TotalSize);

            // Files that are not duplicates (either unique or missing metadata)
            var nonDuplicateFiles = totalFiles - totalDuplicates;

            // Create summary table
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[bold]Metric[/]");
            table.AddColumn("[bold]Value[/]");

            table.AddRow("Total files in database", totalFiles.ToString("N0"));
            table.AddRow("Files with complete metadata", totalDuplicates.ToString("N0"));
            table.AddRow("Files without complete metadata", nonDuplicateFiles.ToString("N0"));
            table.AddEmptyRow();
            table.AddRow("[yellow]Duplicate groups[/]", $"[yellow]{uniqueGroups:N0}[/]");
            table.AddRow("[yellow]Total duplicate files[/]", $"[yellow]{totalDuplicates:N0}[/]");
            table.AddRow("[yellow]Unique file size[/]", $"[yellow]{FormatBytes(uniqueFileSize)}[/]");
            table.AddRow("[yellow]Total duplicate size[/]", $"[yellow]{FormatBytes(totalDuplicateSize)}[/]");

            if (uniqueFileSize > 0)
            {
                var wastedSpace = totalDuplicateSize - uniqueFileSize;
                table.AddRow("[red]Wasted space[/]", $"[red]{FormatBytes(wastedSpace)}[/]");
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // Additional info
            AnsiConsole.MarkupLine("[dim]Note: Duplicates are identified by matching size and metadata timestamp.[/]");
            AnsiConsole.MarkupLine("[dim]Files without metadata timestamp are not included in duplicate detection.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
