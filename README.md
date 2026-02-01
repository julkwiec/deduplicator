# Deduplicator

A command-line application for photo and video file deduplication using .NET and SQLite.

## Features

- **Recursive Directory Scanning**: Scans directories recursively for photo and video files
- **Metadata Extraction**: Extracts EXIF data from photos and metadata from videos (timestamp)
- **Filename Timestamp Parsing**: Automatically extracts timestamps from filenames using common naming conventions from Android, iOS, WhatsApp, and other sources
- **Duplicate Detection**: Identifies duplicates based on file size and metadata timestamp
- **Resume Capability**: Interrupted scans can be resumed from where they left off
- **Progress Tracking**: Real-time progress bar with file processing statistics
- **Multi-Drive Support**: Automatically detects and tracks files across different drives and partitions
- **Incremental Scanning**: Re-scanning directories updates existing entries and removes missing files

## Supported File Formats

### Photos
`.jpg`, `.jpeg`, `.png`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.heic`, `.heif`, `.webp`

### Videos
`.mp4`, `.mov`, `.avi`, `.mkv`, `.wmv`, `.flv`, `.m4v`, `.mpg`, `.mpeg`, `.3gp`, `.webm`

## System Requirements

- Windows 10 or later
- .NET 10.0 Runtime (for development)
- FFmpeg (automatically handled by FFMpegCore package)

## Installation

### From Source

1. Clone or download the source code
2. Build the project:
   ```bash
   dotnet build
   ```
3. Run the application:
   ```bash
   dotnet run -- <command> [options]
   ```

### Building Single-File Executable

To create a single-file, self-contained executable:

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true
```

The executable will be created in: `bin\Release\net10.0\win-x64\publish\Deduplicator.exe`

## Usage

### Basic Commands

```bash
deduplicator scan <directory> [options]
deduplicator summary [options]
deduplicator prepare [options]
deduplicator deduplicate [options]
deduplicator help
```

### Scan Command

Scans a directory recursively for photo and video files:

```bash
deduplicator scan D:\Photos
```

**Options:**
- `--db, -d <path>`: Path to the SQLite database file (default: `./deduplicator.db`)
- `--force-restart, -f`: Force restart instead of resuming incomplete scan

**Examples:**
```bash
# Scan with default database
deduplicator scan D:\Photos

# Scan with custom database
deduplicator scan D:\Photos --db myfiles.db

# Force restart an incomplete scan
deduplicator scan D:\Photos --force-restart
```

### Resume Functionality

If a scan is interrupted (application crash, Ctrl+C, etc.), the next time you run the scan command for the same directory, you'll be prompted to resume:

```
Found incomplete scan session from 2026-01-26 14:23:15
  Progress: 1,247 of ~3,500 files

Resume this scan? [Y/n]:
```

Choose 'Y' to continue from where it left off, or 'n' to start a fresh scan.

### Summary Command

Displays duplicate file statistics:

```bash
deduplicator summary
```

**Options:**
- `--db, -d <path>`: Path to the SQLite database file (default: `./deduplicator.db`)

**Example Output:**
```
┌────────────────────────────────────┬────────────┐
│ Metric                             │ Value      │
├────────────────────────────────────┼────────────┤
│ Total files in database            │ 3,500      │
│ Files with complete metadata       │ 3,200      │
│ Files without complete metadata    │ 300        │
│                                    │            │
│ Duplicate groups                   │ 150        │
│ Total duplicate files              │ 450        │
│ Unique file size                   │ 2.5 GB     │
│ Total duplicate size               │ 7.5 GB     │
│ Wasted space                       │ 5.0 GB     │
└────────────────────────────────────┴────────────┘
```

### Prepare Command

Analyzes the database and prepares a task list for deduplication:

```bash
deduplicator prepare
```

**Options:**
- `--db, -d <path>`: Path to the SQLite database file (default: `./deduplicator.db`)

**What it does:**
1. Finds duplicate groups based on matching file size and metadata MD5
2. For each duplicate group, determines the lowest timestamp from all available timestamp sources (metadata, filename, filesystem)
3. Creates an "adjust" task for one file in each group (to update timestamps and rename)
4. Creates "delete" tasks for all remaining duplicates in each group

**Example Output:**
```
┌────────────────────────────────┬────────────┐
│ Metric                         │ Value      │
├────────────────────────────────┼────────────┤
│ Duplicate groups found         │ 150        │
│ Total duplicate files          │ 450        │
│ Files to adjust                │ 150        │
│ Files to delete                │ 300        │
│ Total tasks created            │ 450        │
└────────────────────────────────┴────────────┘
```

### Deduplicate Command

Executes the tasks prepared by the `prepare` command:

```bash
deduplicator deduplicate
```

**Options:**
- `--db, -d <path>`: Path to the SQLite database file (default: `./deduplicator.db`)

**What it does:**
1. Groups tasks by device (drive/partition)
2. For each device:
   - Checks if the device is currently connected
   - If not connected, prompts you to connect it
   - Executes all tasks for that device with a progress bar
3. For "adjust" tasks:
   - Sets file creation and modification timestamps to the lowest found timestamp
   - Renames the file to include a timestamp prefix (YYYYMMDD_HHMMSS_originalname.ext) if not already present
4. For "delete" tasks:
   - Deletes the duplicate file
5. Removes each completed task from the database

**Important Notes:**
- Each task is executed in a transaction - if the task fails, it remains in the database
- You can safely interrupt and resume the deduplicate process
- Files are permanently deleted - make sure you have backups before running!

**Example:**
```bash
# Review what will be done
deduplicator prepare

# Execute the deduplication
deduplicator deduplicate
```

## How Duplicate Detection Works

Deduplicator identifies duplicate files using two criteria:

1. **File Size**: The size of the file in bytes
2. **Metadata Timestamp**: The creation time extracted from EXIF (photos) or video metadata

Files are considered duplicates if both values match. Files without metadata timestamp are not included in duplicate detection.

## Filename Timestamp Extraction

The application automatically extracts timestamps from filenames that follow common naming conventions used by mobile devices, cameras, and messaging apps. This provides an additional data point for identifying when a photo or video was captured.

### Supported Filename Patterns

**Android Native Camera:**
- `IMG_20230115_143052.jpg` or `VID_20230115_143052.mp4`
- `IMG_20230115_143052123.jpg` (with milliseconds)

**WhatsApp:**
- Android: `IMG-20230115-WA0001.jpg` (date only, no time)
- iOS/Desktop: `WhatsApp Image 2023-01-15 at 14.30.52.jpeg`
- iOS Share Sheet: `PHOTO-2023-01-15-14-30-52.jpg`

**Screenshots:**
- Android: `Screenshot_20230115-143052.png`
- iOS: `Screenshot 2023-01-15 at 14.30.52.png`

**Signal:**
- `signal-2023-01-15-143052.jpg`

**Generic Patterns:**
- `20230115_143052.jpg` (compact format without prefix)
- `2023-01-15_14-30-52.jpg` (dashed format)
- `YYYYMMDD` or `YYYY-MM-DD` (date only formats)

The filename timestamp is stored separately from the EXIF/video metadata timestamp, providing redundancy and helping to recover date information when EXIF data is missing or corrupted.

## Database Schema

The application uses SQLite with the following structure:

### Container Table
Stores drive/partition identifiers to track files across different physical devices.

### File Table
Stores metadata for each scanned file:
- Path (relative to partition root)
- File size
- Metadata timestamp (from EXIF/video metadata)
- Filename timestamp (extracted from filename pattern)
- Filesystem creation time
- Filesystem modified time
- Metadata MD5 hash
- Last scan session reference

### ScanSession Table
Tracks scan operations for resume capability:
- Container and root path
- Status (in_progress, completed, failed)
- Start and completion timestamps
- Files processed count

### FileTask Table
Stores deduplication tasks prepared by the `prepare` command:
- File reference (foreign key to Files table)
- Operation type ("adjust" or "delete")
- New timestamp (for adjust operations - the lowest timestamp found across all duplicates)
- Tasks are removed from the table after successful execution

## Performance Notes

- Files are processed in batches of 100 for optimal performance
- Database commits occur after each batch to enable resume capability
- Progress bar shows real-time processing speed (files/sec)
- Metadata extraction can be slow for large video files

## Limitations

- Windows-only (due to WMI-based drive detection)
- Requires administrator or appropriate WMI permissions for drive/partition identification
- FFmpeg must be available for video metadata extraction
- EF Core trimming may cause some reflection-based features to fail in trimmed builds

## Troubleshooting

### "Could not find partition information for drive X:"
- Ensure you're running with appropriate permissions
- The drive must be a physical partition (network drives not supported)

### Video metadata extraction fails
- FFmpeg binaries are automatically downloaded by FFMpegCore
- Check internet connectivity on first run

### Build warnings about trimming
- The IL2026 warning about EF Core trimming is expected
- The application uses `TrimMode=partial` to minimize issues
- Full trimming may cause runtime errors with EF Core

## License

This application was created as a utility tool. Feel free to modify and distribute as needed.

## Technical Stack

- **.NET 10.0**: Core framework
- **Entity Framework Core**: Database ORM
- **SQLite**: Embedded database
- **Spectre.Console**: Rich terminal UI and progress bars
- **MetadataExtractor**: EXIF and photo metadata reading
- **FFMpegCore**: Video metadata extraction
- **System.Management**: WMI integration for drive/partition detection
