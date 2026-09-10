namespace CamledianDrive.Services;

public interface IMountService
{
    Task MountAsync(string username, string password, CancellationToken cancellationToken = default);
    Task UnmountAsync(CancellationToken cancellationToken = default);
    Task<bool> IsMountedAsync(CancellationToken cancellationToken = default);
}
