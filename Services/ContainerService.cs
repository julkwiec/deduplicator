using System.Management;
using System.Text.RegularExpressions;

namespace Deduplicator.Services;

public class ContainerService : IContainerService
{
    private readonly Dictionary<char, (string? partitionGuid, string diskId)> _cache = new();

    public async Task<(string? partitionGuid, string diskId)> GetContainerInfoAsync(string path)
    {
        var driveLetter = System.IO.Path.GetPathRoot(path)?.TrimEnd('\\', ':').ToUpperInvariant();
        if (string.IsNullOrEmpty(driveLetter) || driveLetter.Length != 1)
        {
            throw new ArgumentException($"Cannot determine drive letter from path: {path}");
        }

        var driveChar = driveLetter[0];

        // Check cache
        if (_cache.TryGetValue(driveChar, out var cached))
        {
            return cached;
        }

        // Query WMI for partition information
        var result = await Task.Run(() => GetPartitionInfoAsync(driveChar));
        _cache[driveChar] = result;
        return result;
    }

    private (string? partitionGuid, string diskId) GetPartitionInfoAsync(char driveLetter)
    {
        try
        {
            var driveLetterWithColon = $"{driveLetter}:";

            // Step 1: Get volume GUID from Win32_Volume
            string? volumeGuid = null;
            var query = $"SELECT * FROM Win32_Volume WHERE DriveLetter = '{driveLetterWithColon}'";

            using (var searcher = new ManagementObjectSearcher(query))
            {
                foreach (ManagementObject volume in searcher.Get())
                {
                    var deviceId = volume["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        // DeviceID format: \\?\Volume{GUID}\
                        // Extract just the GUID part
                        var match = Regex.Match(deviceId, @"\{([0-9a-fA-F\-]+)\}");
                        if (match.Success)
                        {
                            volumeGuid = match.Groups[1].Value;
                        }
                        else
                        {
                            // Use the whole DeviceID if we can't extract GUID
                            volumeGuid = deviceId;
                        }
                    }
                    break;
                }
            }

            // Step 2: Get partition information
            string? partitionPath = null;
            int diskIndex = -1;
            int partitionIndex = -1;

            query = $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{driveLetterWithColon}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";

            using (var searcher = new ManagementObjectSearcher(query))
            {
                foreach (ManagementObject partition in searcher.Get())
                {
                    partitionPath = partition["__PATH"]?.ToString();

                    // Get DiskIndex and Index (partition number) for fallback identifier
                    if (partition["DiskIndex"] != null)
                    {
                        diskIndex = Convert.ToInt32(partition["DiskIndex"]);
                    }
                    if (partition["Index"] != null)
                    {
                        partitionIndex = Convert.ToInt32(partition["Index"]);
                    }

                    break;
                }
            }

            if (string.IsNullOrEmpty(partitionPath))
            {
                throw new InvalidOperationException($"Could not find partition for drive {driveLetter}:");
            }

            // Step 3: Get physical disk information
            query = $"ASSOCIATORS OF {{{partitionPath}}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";

            string? diskPath = null;
            using (var searcher = new ManagementObjectSearcher(query))
            {
                foreach (ManagementObject disk in searcher.Get())
                {
                    diskPath = disk["__PATH"]?.ToString();
                    break;
                }
            }

            if (string.IsNullOrEmpty(diskPath))
            {
                throw new InvalidOperationException($"Could not find disk for drive {driveLetter}:");
            }

            // Step 4: Get disk unique identifier
            string diskId;
            using (var disk = new ManagementObject(diskPath))
            {
                // Try SerialNumber first (most reliable for USB drives)
                var serialNumber = disk["SerialNumber"]?.ToString()?.Trim(' ', '.', '\0');

                if (!string.IsNullOrEmpty(serialNumber))
                {
                    diskId = serialNumber;
                }
                else
                {
                    // Fallback: Use Model + Signature or PNPDeviceID
                    var model = disk["Model"]?.ToString()?.Trim();
                    var signature = disk["Signature"]?.ToString();
                    var pnpDeviceId = disk["PNPDeviceID"]?.ToString();

                    if (!string.IsNullOrEmpty(signature))
                    {
                        diskId = $"{model}_{signature}";
                    }
                    else if (!string.IsNullOrEmpty(pnpDeviceId))
                    {
                        diskId = pnpDeviceId;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Could not determine disk unique ID for drive {driveLetter}:");
                    }
                }
            }

            // Use volume GUID if available, otherwise construct from disk index + partition index
            string? finalPartitionGuid = volumeGuid;
            if (string.IsNullOrEmpty(finalPartitionGuid) && diskIndex >= 0 && partitionIndex >= 0)
            {
                // Fallback: use disk_index:partition_index
                finalPartitionGuid = $"Disk{diskIndex}:Partition{partitionIndex}";
            }

            return (finalPartitionGuid, diskId);
        }
        catch (ManagementException ex)
        {
            throw new InvalidOperationException($"WMI error querying drive {driveLetter}: {ex.Message}", ex);
        }
    }
}
