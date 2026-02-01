using Deduplicator.Commands;

namespace Deduplicator;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "scan":
                return await ExecuteScanCommand(args.Skip(1).ToArray());

            case "summary":
                return await ExecuteSummaryCommand(args.Skip(1).ToArray());

            case "prepare":
                return await ExecutePrepareCommand(args.Skip(1).ToArray());

            case "deduplicate":
                return await ExecuteDeduplicateCommand(args.Skip(1).ToArray());

            case "help":
            case "--help":
            case "-h":
                ShowHelp();
                return 0;

            default:
                Console.WriteLine($"Unknown command: {command}");
                Console.WriteLine();
                ShowHelp();
                return 1;
        }
    }

    static async Task<int> ExecuteScanCommand(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Error: Directory argument is required.");
            Console.WriteLine();
            Console.WriteLine("Usage: deduplicator scan <directory> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --db, -d <path>        Path to the SQLite database file (default: ./deduplicator.db)");
            Console.WriteLine("  --force-restart, -f    Force restart instead of resuming incomplete scan");
            return 1;
        }

        var directory = args[0];
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "deduplicator.db");
        var forceRestart = false;

        // Parse options
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db":
                case "-d":
                    if (i + 1 < args.Length)
                    {
                        dbPath = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: --db option requires a value.");
                        return 1;
                    }
                    break;

                case "--force-restart":
                case "-f":
                    forceRestart = true;
                    break;

                default:
                    Console.WriteLine($"Error: Unknown option: {args[i]}");
                    return 1;
            }
        }

        return await ScanCommand.ExecuteAsync(directory, dbPath, forceRestart);
    }

    static async Task<int> ExecuteSummaryCommand(string[] args)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "deduplicator.db");

        // Parse options
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db":
                case "-d":
                    if (i + 1 < args.Length)
                    {
                        dbPath = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: --db option requires a value.");
                        return 1;
                    }
                    break;

                default:
                    Console.WriteLine($"Error: Unknown option: {args[i]}");
                    return 1;
            }
        }

        return await SummaryCommand.ExecuteAsync(dbPath);
    }

    static async Task<int> ExecutePrepareCommand(string[] args)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "deduplicator.db");

        // Parse options
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db":
                case "-d":
                    if (i + 1 < args.Length)
                    {
                        dbPath = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: --db option requires a value.");
                        return 1;
                    }
                    break;

                default:
                    Console.WriteLine($"Error: Unknown option: {args[i]}");
                    return 1;
            }
        }

        return await PrepareCommand.ExecuteAsync(dbPath);
    }

    static async Task<int> ExecuteDeduplicateCommand(string[] args)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "deduplicator.db");

        // Parse options
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--db":
                case "-d":
                    if (i + 1 < args.Length)
                    {
                        dbPath = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: --db option requires a value.");
                        return 1;
                    }
                    break;

                default:
                    Console.WriteLine($"Error: Unknown option: {args[i]}");
                    return 1;
            }
        }

        return await DeduplicateCommand.ExecuteAsync(dbPath);
    }

    static void ShowHelp()
    {
        Console.WriteLine("Deduplicator - Photo and video file deduplication tool");
        Console.WriteLine();
        Console.WriteLine("Usage: deduplicator <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  scan <directory>    Scan directories for photo and video files");
        Console.WriteLine("  summary             Show duplicate file summary");
        Console.WriteLine("  prepare             Prepare tasks for duplicate file management");
        Console.WriteLine("  deduplicate         Execute prepared deduplication tasks");
        Console.WriteLine("  help                Show this help message");
        Console.WriteLine();
        Console.WriteLine("Scan Options:");
        Console.WriteLine("  --db, -d <path>     Path to the SQLite database file (default: ./deduplicator.db)");
        Console.WriteLine("  --force-restart, -f Force restart instead of resuming incomplete scan");
        Console.WriteLine();
        Console.WriteLine("Summary Options:");
        Console.WriteLine("  --db, -d <path>     Path to the SQLite database file (default: ./deduplicator.db)");
        Console.WriteLine();
        Console.WriteLine("Prepare Options:");
        Console.WriteLine("  --db, -d <path>     Path to the SQLite database file (default: ./deduplicator.db)");
        Console.WriteLine();
        Console.WriteLine("Deduplicate Options:");
        Console.WriteLine("  --db, -d <path>     Path to the SQLite database file (default: ./deduplicator.db)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  deduplicator scan D:\\Photos");
        Console.WriteLine("  deduplicator scan D:\\Photos --db mydb.db");
        Console.WriteLine("  deduplicator summary");
        Console.WriteLine("  deduplicator summary --db mydb.db");
        Console.WriteLine("  deduplicator prepare");
        Console.WriteLine("  deduplicator prepare --db mydb.db");
        Console.WriteLine("  deduplicator deduplicate");
        Console.WriteLine("  deduplicator deduplicate --db mydb.db");
    }
}
