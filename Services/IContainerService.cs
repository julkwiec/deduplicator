namespace Deduplicator.Services;

public interface IContainerService
{
    /// <summary>
    /// Gets the container (partition and disk) information for a given path
    /// </summary>
    Task<(string? partitionGuid, string diskId)> GetContainerInfoAsync(string path);
}
