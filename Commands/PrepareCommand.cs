using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using Deduplicator.Data;

namespace Deduplicator.Commands;

public class PrepareCommand
{
    public static async Task<int> ExecuteAsync(string dbPath)
    {
        try
        {
            // Initialize database
            using var context = new DeduplicatorContext(dbPath);
            context.EnsureCreated();

            AnsiConsole.MarkupLine("[cyan]Preparing tasks for duplicate file management...[/]");
            AnsiConsole.WriteLine();

            // Clear existing tasks
            await context.Database.ExecuteSqlRawAsync("DELETE FROM FileTasks");
            AnsiConsole.MarkupLine("[dim]Cleared existing tasks table[/]");

            // Find duplicate groups based on Size and MetadataMd5
            // Only consider files where MetadataMd5 is not null
            var duplicateGroups = await context.Files
                .Where(f => f.MetadataMd5 != null && f.MetadataMd5 != "")
                .GroupBy(f => new { f.Size, f.MetadataMd5 })
                .Where(g => g.Count() > 1)
                .Select(g => new
                {
                    Key = g.Key,
                    Files = g.Select(f => new
                    {
                        f.Id,
                        f.MetadataTimestamp,
                        f.FilenameTimestamp,
                        f.FilesystemCreationTime,
                        f.FilesystemModifiedTime
                    }).ToList()
                })
                .ToListAsync();

            if (duplicateGroups.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No duplicate groups found in the database.[/]");
                AnsiConsole.MarkupLine("[yellow]Run a scan first to populate the database.[/]");
                return 0;
            }

            int totalDuplicates = 0;
            int tasksCreated = 0;

            foreach (var group in duplicateGroups)
            {
                var files = group.Files;
                totalDuplicates += files.Count;

                // Find the lowest available timestamp among all timestamp columns
                long? lowestTimestamp = null;

                foreach (var file in files)
                {
                    var timestamps = new[] {
                        file.MetadataTimestamp,
                        file.FilenameTimestamp,
                        file.FilesystemCreationTime,
                        file.FilesystemModifiedTime
                    };

                    foreach (var timestamp in timestamps)
                    {
                        if (timestamp.HasValue)
                        {
                            if (!lowestTimestamp.HasValue || timestamp.Value < lowestTimestamp.Value)
                            {
                                lowestTimestamp = timestamp.Value;
                            }
                        }
                    }
                }

                // Pick the first file to adjust, rest to delete
                bool isFirst = true;
                foreach (var file in files)
                {
                    var task = new Data.Models.FileTask
                    {
                        FileId = file.Id,
                        Operation = isFirst ? "adjust" : "delete",
                        NewTimestamp = lowestTimestamp
                    };

                    context.FileTasks.Add(task);
                    tasksCreated++;
                    isFirst = false;
                }
            }

            await context.SaveChangesAsync();

            // Display summary
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[cyan]Metric[/]");
            table.AddColumn("[cyan]Value[/]");

            table.AddRow("Duplicate groups found", duplicateGroups.Count.ToString("N0"));
            table.AddRow("Total duplicate files", totalDuplicates.ToString("N0"));
            table.AddRow("Files to adjust", duplicateGroups.Count.ToString("N0"));
            table.AddRow("Files to delete", (totalDuplicates - duplicateGroups.Count).ToString("N0"));
            table.AddRow("Total tasks created", tasksCreated.ToString("N0"));

            AnsiConsole.WriteLine();
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[green]Tasks prepared successfully![/]");
            AnsiConsole.MarkupLine("[dim]Note: Tasks are based on matching file size and metadata MD5.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            return 1;
        }
    }
}
