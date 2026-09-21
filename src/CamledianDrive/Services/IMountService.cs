namespace CamledianDrive.Services;

public interface IMountService
{
    Task MountAsync(string username, string password, CancellationToken cancellationToken = default);
    Task UnmountAsync(CancellationToken cancellationToken = default, bool force = false);
    Task<bool> IsMountedAsync(CancellationToken cancellationToken = default);

    Task<TransferStatus> GetTransferStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The drive letter (e.g. "X:") currently used by the mount, or null when
    /// not mounted or not yet determined. Populated as a side effect of
    /// <see cref="IsMountedAsync"/> and <see cref="MountAsync"/>.
    /// </summary>
    string? CurrentDriveLetter { get; }
}
