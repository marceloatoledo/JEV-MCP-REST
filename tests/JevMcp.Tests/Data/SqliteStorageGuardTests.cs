using JevMcp.Data;

namespace JevMcp.Tests.Data;

public sealed class SqliteStorageGuardTests
{
    private const string OverlayOnly = """
        1623 1597 0:187 / / rw,relatime - overlay overlay rw
        1624 1623 0:190 / /proc rw,nosuid,nodev,noexec,relatime - proc proc rw
        1635 1623 0:191 / /dev rw,nosuid - tmpfs tmpfs rw
        """;

    private const string NamedVolume = """
        1623 1597 0:187 / / rw,relatime - overlay overlay rw
        1624 1623 0:190 / /proc rw,nosuid,nodev,noexec,relatime - proc proc rw
        1648 1623 8:3 /var/lib/docker/volumes/jevmcp_jevmcp-data/_data /app/data rw,relatime - ext4 /dev/sda3 rw
        """;

    [Fact]
    public void OutsideAContainerDoesNotWarn()
    {
        Assert.False(SqliteStorageGuard.IsEphemeral("/app/data/JevMcp.db", runningInContainer: false, OverlayOnly));
    }

    [Fact]
    public void ContainerWithoutAVolumeWarns()
    {
        Assert.True(SqliteStorageGuard.IsEphemeral("/app/data/JevMcp.db", runningInContainer: true, OverlayOnly));
        Assert.True(SqliteStorageGuard.IsEphemeral("/app/data/JevMcp.db", runningInContainer: true, mountInfo: null));
    }

    [Fact]
    public void ContainerWithAppDataVolumeDoesNotWarn()
    {
        Assert.False(SqliteStorageGuard.IsEphemeral("/app/data/JevMcp.db", runningInContainer: true, NamedVolume));
    }

    [Fact]
    public void MountPointWithEscapedSpaceIsRecognized()
    {
        var mountInfo = "1648 1623 8:3 / /mnt/data\\040volume rw - ext4 /dev/sda3 rw\n";
        Assert.True(SqliteStorageGuard.IsOnNonRootMount("/mnt/data volume", mountInfo));
        Assert.Equal("/mnt/data volume", SqliteStorageGuard.ParseMountPoint(mountInfo.Trim()));
    }

    [Fact]
    public void DatabaseDirectoryIgnoresTheOsSeparator()
    {
        Assert.Equal("/app/data", SqliteStorageGuard.DataDirectory("/app/data/JevMcp.db"));
        Assert.Equal("/app/data", SqliteStorageGuard.DataDirectory(@"\app\data\JevMcp.db"));
    }
}
