using Portal.Core.Connections;
using Portal.Core.Files;

namespace Portal.Tests;

public sealed class PermissionsTests
{
    [Theory]
    [InlineData(0x1ED, "rwxr-xr-x", "755")]
    [InlineData(0x1A4, "rw-r--r--", "644")]
    [InlineData(0x1C0, "rwx------", "700")]
    [InlineData(0x9ED, "rwsr-xr-x", "4755")]
    [InlineData(0x3FF, "rwxrwxrwt", "1777")]
    public void Formats_symbolic_and_octal(int mode, string symbolic, string octal)
    {
        Assert.Equal(symbolic, Permissions.ToSymbolic(mode));
        Assert.Equal(octal, Permissions.ToOctal(mode));
        Assert.True(Permissions.TryParseOctal(octal, out var parsed));
        Assert.Equal(mode, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("78")]
    [InlineData("12345")]
    [InlineData("rwx")]
    public void Rejects_bad_octal(string text) => Assert.False(Permissions.TryParseOctal(text, out _));

    [Fact]
    public void Describes_in_plain_words()
    {
        Assert.Equal(["Owner can read, write, run", "Group can read", "Everyone has no access"], Permissions.Describe(0x1E0));
    }
}

public sealed class RemotePathTests
{
    [Theory]
    [InlineData("/", "a", "/a")]
    [InlineData("/mnt/user", "media", "/mnt/user/media")]
    [InlineData("/mnt/user/", "media", "/mnt/user/media")]
    public void Combines(string dir, string name, string expected) => Assert.Equal(expected, RemotePath.Combine(dir, name));

    [Theory]
    [InlineData("/mnt/user/media", "/mnt/user")]
    [InlineData("/mnt", "/")]
    [InlineData("/", null)]
    public void Finds_the_parent(string path, string? expected) => Assert.Equal(expected, RemotePath.Parent(path));

    [Theory]
    [InlineData("//mnt//user/./media/../appdata/", "/mnt/user/appdata")]
    [InlineData("/..", "/")]
    public void Normalizes(string path, string expected) => Assert.Equal(expected, RemotePath.Normalize(path));

    [Fact]
    public void Knows_what_lies_inside_a_folder()
    {
        Assert.True(RemotePath.IsWithin("/mnt/user/media", "/mnt/user"));
        Assert.False(RemotePath.IsWithin("/mnt/username", "/mnt/user"));
        Assert.True(RemotePath.IsWithin("/anything", "/"));
    }
}

public sealed class ServerOutputParsingTests
{
    [Fact]
    public void Df_keeps_real_storage_and_drops_virtual_mounts()
    {
        const string df = """
            Filesystem     1024-blocks       Used  Available Capacity Mounted on
            rootfs             8023452    1203452    6820000      15% /
            tmpfs                32768       1200      31568       4% /run
            /dev/sda1         31248896     700000   30548896       3% /boot
            shfs           23437500000 20156250000 3281250000      86% /mnt/user
            /dev/nvme0n1p1   976762584  400472659  576289925      41% /mnt/cache
            overlay          104857600   50000000   54857600      48% /var/lib/docker/overlay2/abc/merged
            """;
        var disks = DiskUsage.ParseDf(df);
        Assert.Equal(["/boot", "/mnt/user", "/mnt/cache"], disks.Select(d => d.MountPoint));
        Assert.Equal(0.86, disks[1].UsedFraction, 2);
        Assert.Equal(3281250000L * 1024, disks[1].FreeBytes);
    }

    [Fact]
    public void Account_names_map_ids_from_passwd_and_group()
    {
        var names = AccountNames.Parse("root:x:0:0:root:/root:/bin/bash\nnobody:x:99:100::/:/bin/false\n" + AccountNames.Separator + "\nroot:x:0:\nusers:x:100:\n");
        Assert.Equal("nobody", names.User(99));
        Assert.Equal("users", names.Group(100));
        Assert.Equal("1234", names.User(1234));
        Assert.Equal(["nobody", "root"], names.UserNames);
    }

    [Fact]
    public void Find_output_becomes_entries()
    {
        var entries = RemoteFileSystem.ParseFind("d\t4096\t1700000000.5\t/mnt/user/media/Movies\nf\t1234\t1700000100.0\t/mnt/user/media/Movies/a b.mkv\n");
        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal("a b.mkv", entries[1].Name);
        Assert.Equal(1234, entries[1].Size);
    }

    [Fact]
    public void Glob_characters_in_search_text_are_escaped()
    {
        Assert.Equal(@"report\[1\]\*", RemoteFileSystem.EscapeGlob("report[1]*"));
    }
}

public sealed class PermissionDeltaTests
{
    [Fact]
    public void Only_the_changed_bits_are_applied()
    {
        var delta = PermissionDelta.Between(0x1ED, 0x1FD);
        Assert.Equal("g+w", delta.ToChmod());
        Assert.Equal(0x1D0, delta.ApplyTo(0x1C0));
        Assert.Equal(0x1B4, delta.ApplyTo(0x1A4));
    }

    [Fact]
    public void Removals_and_special_bits_are_spelled_out()
    {
        Assert.Equal("u+x,g+x,g-w,o-rw", PermissionDelta.Between(0x1B6, 0x1E8).ToChmod());
        Assert.Equal("u+s,o+t", PermissionDelta.Between(0x1ED, 0xBED).ToChmod());
        Assert.True(PermissionDelta.Between(0x1A4, 0x1A4).IsEmpty);
    }
}

public sealed class ServerCopyNameTests
{
    [Fact]
    public void Copies_are_named_like_explorer_does()
    {
        Assert.Equal("report - Copy.pdf", ServerCopy.CopyName(new HashSet<string> { "report.pdf" }, "report.pdf"));
        Assert.Equal("report - Copy (2).pdf", ServerCopy.CopyName(new HashSet<string> { "report.pdf", "report - Copy.pdf" }, "report.pdf"));
        Assert.Equal("site - Copy", ServerCopy.CopyName(new HashSet<string> { "site" }, "site"));
    }
}
