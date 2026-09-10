namespace CamledianDrive.Services;

public sealed class RcloneMountService : IMountService
{
    public Task MountAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("rclone integration will be added after Commander authentication is reviewed.");
    }

    public Task UnmountAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> IsMountedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
