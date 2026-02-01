using Microsoft.EntityFrameworkCore;
using Deduplicator.Data;
using Deduplicator.Data.Models;

namespace Deduplicator.Services;

public class ScanSessionManager
{
    private readonly DeduplicatorContext _context;

    public ScanSessionManager(DeduplicatorContext context)
    {
        _context = context;
    }

    public async Task<ScanSession?> FindIncompleteScanAsync(int containerId, string rootPath)
    {
        return await _context.ScanSessions
            .FirstOrDefaultAsync(s =>
                s.ContainerId == containerId &&
                s.RootPath == rootPath &&
                s.Status == "in_progress");
    }

    public async Task<ScanSession> CreateScanSessionAsync(int containerId, string rootPath, int? totalFiles = null)
    {
        var session = new ScanSession
        {
            ContainerId = containerId,
            RootPath = rootPath,
            Status = "in_progress",
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            FilesTotal = totalFiles,
            FilesProcessed = 0
        };

        _context.ScanSessions.Add(session);
        await _context.SaveChangesAsync();

        return session;
    }

    public async Task UpdateProgressAsync(int sessionId, int filesProcessed, int? totalFiles = null)
    {
        var session = await _context.ScanSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.FilesProcessed = filesProcessed;
            if (totalFiles.HasValue)
            {
                session.FilesTotal = totalFiles.Value;
            }
            await _context.SaveChangesAsync();
        }
    }

    public async Task CompleteScanAsync(int sessionId)
    {
        var session = await _context.ScanSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = "completed";
            session.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _context.SaveChangesAsync();
        }
    }

    public async Task FailScanAsync(int sessionId)
    {
        var session = await _context.ScanSessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Status = "failed";
            session.CompletedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _context.SaveChangesAsync();
        }
    }

    public async Task<HashSet<string>> GetProcessedFilesAsync(int sessionId)
    {
        // Get all file paths that were processed in this scan session
        var files = await _context.Files
            .Where(f => f.LastScanSessionId == sessionId)
            .Select(f => System.IO.Path.Combine(f.Path, f.Name))
            .ToListAsync();

        return new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
    }

    public async Task CleanupOrphanedFilesAsync(int sessionId, int containerId, string rootPath)
    {
        // Find files that belong to this container and root path but weren't touched in this scan
        var orphanedFiles = await _context.Files
            .Where(f =>
                f.ContainerId == containerId &&
                f.Path.StartsWith(rootPath) &&
                (f.LastScanSessionId == null || f.LastScanSessionId != sessionId))
            .ToListAsync();

        if (orphanedFiles.Any())
        {
            _context.Files.RemoveRange(orphanedFiles);
            await _context.SaveChangesAsync();
        }
    }
}
