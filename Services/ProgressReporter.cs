using Spectre.Console;

namespace Deduplicator.Services;

public class ProgressReporter : IDisposable
{
    private ProgressContext? _progressContext;
    private ProgressTask? _progressTask;
    private readonly DateTime _startTime;
    private int _filesProcessed;

    public ProgressReporter()
    {
        _startTime = DateTime.Now;
    }

    public void StartDiscovery()
    {
        AnsiConsole.MarkupLine("[yellow]Discovering files...[/]");
    }

    public void StartProcessing(int totalFiles)
    {
        AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .Start(ctx =>
            {
                _progressContext = ctx;
                _progressTask = ctx.AddTask("[green]Processing files[/]", maxValue: totalFiles);

                // Keep the progress alive until disposed
                while (!_progressTask.IsFinished)
                {
                    Thread.Sleep(100);
                }
            });
    }

    public void UpdateProgress(int filesProcessed, int totalFiles, string? currentFile = null)
    {
        _filesProcessed = filesProcessed;

        if (_progressTask != null)
        {
            _progressTask.Value = filesProcessed;

            var elapsed = DateTime.Now - _startTime;
            var filesPerSecond = elapsed.TotalSeconds > 0 ? filesProcessed / elapsed.TotalSeconds : 0;

            var description = $"[green]Processing files[/] ({filesProcessed}/{totalFiles})";
            if (!string.IsNullOrEmpty(currentFile))
            {
                var shortPath = currentFile.Length > 50 ? "..." + currentFile.Substring(currentFile.Length - 47) : currentFile;
                description += $" - {shortPath}";
            }
            if (filesPerSecond > 0)
            {
                description += $" [{filesPerSecond:F1} files/sec]";
            }

            _progressTask.Description = description;
        }
    }

    public void ReportDiscovery(int filesFound)
    {
        AnsiConsole.MarkupLine($"[green]Found {filesFound} files to process[/]");
    }

    public void Complete(int filesProcessed)
    {
        var elapsed = DateTime.Now - _startTime;
        AnsiConsole.MarkupLine($"[green]Scan completed![/] Processed {filesProcessed} files in {elapsed.TotalSeconds:F1}s");
    }

    public void ReportError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {message}");
    }

    public void ReportWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]Warning:[/] {message}");
    }

    public void Dispose()
    {
        // Progress context is disposed automatically
    }
}
