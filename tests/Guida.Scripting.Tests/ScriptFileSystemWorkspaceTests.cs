using System.Text;
using Guida.Scripting;

namespace Guida.Scripting.Tests;

public sealed class ScriptFileSystemWorkspaceTests
{
    [Fact]
    public void Constructor_canonicalizes_root_and_can_create_it()
    {
        using var temp = TemporaryDirectory.CreateMissing();
        var root = Path.Combine(temp.Path, ".");

        var workspace = new ScriptFileSystemWorkspace(
            root,
            new ScriptFileSystemWorkspaceOptions { CreateRoot = true });

        Assert.True(Directory.Exists(temp.Path));
        Assert.Equal(Path.GetFullPath(root), workspace.RootPath);
        Assert.False(workspace.IsReadOnly);
    }

    [Fact]
    public void Constructor_rejects_missing_root_when_create_root_is_false()
    {
        using var temp = TemporaryDirectory.CreateMissing();

        Assert.Throws<DirectoryNotFoundException>(() => new ScriptFileSystemWorkspace(temp.Path));
    }

    [Fact]
    public async Task Workspace_reads_lists_and_stats_entries_under_root()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "scripts"));
        await File.WriteAllTextAsync(
            Path.Combine(temp.Path, "scripts", "job.js"),
            "return 42");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var entry = await workspace.GetEntryAsync("scripts/job.js");
        var content = await workspace.ReadFileAsync(@"scripts\job.js");
        var listing = await workspace.ListAsync("scripts");

        Assert.True(entry.Success);
        Assert.Equal("scripts/job.js", entry.Value?.Path);
        Assert.Equal("job.js", entry.Value?.Name);
        Assert.Equal(ScriptWorkspaceEntryKind.File, entry.Value?.Kind);
        Assert.Equal(Encoding.UTF8.GetByteCount("return 42"), entry.Value?.Length);
        Assert.NotNull(entry.Value?.LastModifiedAt);

        Assert.True(content.Success);
        Assert.Equal("scripts/job.js", content.Value?.Path);
        Assert.Equal("return 42", Encoding.UTF8.GetString(content.Value!.Content.ToArray()));

        var listed = Assert.Single(listing.Value!);
        Assert.Equal("scripts/job.js", listed.Path);
    }

    [Fact]
    public async Task Workspace_returns_logical_paths_not_physical_paths()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "file.txt"), "hello");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var entry = await workspace.GetEntryAsync("file.txt");
        var content = await workspace.ReadFileAsync("file.txt");

        Assert.True(entry.Success);
        Assert.True(content.Success);
        Assert.Equal("file.txt", entry.Value?.Path);
        Assert.Equal("file.txt", content.Value?.Path);
        Assert.DoesNotContain(temp.Path, entry.Value!.Path);
        Assert.DoesNotContain(temp.Path, content.Value!.Path);
    }

    [Fact]
    public async Task List_returns_direct_children_in_deterministic_order()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "root"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "root", "b-dir"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "root", "c.txt"), "c");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "root", "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "root", "b-dir", "nested.txt"), "nested");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var listing = await workspace.ListAsync("root");

        Assert.True(listing.Success);
        Assert.Equal(
            ["root/a.txt", "root/b-dir", "root/c.txt"],
            listing.Value!.Select(entry => entry.Path).ToArray());
    }

    [Fact]
    public async Task Read_missing_file_returns_not_found()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var result = await workspace.ReadFileAsync("missing.txt");

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task Read_directory_returns_not_a_file()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "scripts"));
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var result = await workspace.ReadFileAsync("scripts");

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.NotAFile, result.Error?.Code);
    }

    [Fact]
    public async Task List_file_returns_not_a_directory()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "file.txt"), "hello");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var result = await workspace.ListAsync("file.txt");

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.NotADirectory, result.Error?.Code);
    }

    [Fact]
    public async Task Write_creates_and_overwrites_files_by_default()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var created = await workspace.WriteFileAsync("file.txt", "first"u8.ToArray());
        var overwritten = await workspace.WriteFileAsync("file.txt", "second"u8.ToArray());
        var content = await workspace.ReadFileAsync("file.txt");

        Assert.True(created.Success);
        Assert.True(overwritten.Success);
        Assert.Equal("second", Encoding.UTF8.GetString(content.Value!.Content.ToArray()));
    }

    [Fact]
    public async Task Write_rejects_overwrite_when_disabled()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "file.txt"), "original");
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var result = await workspace.WriteFileAsync(
            "file.txt",
            "new"u8.ToArray(),
            new ScriptWorkspaceWriteOptions { Overwrite = false });

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.AlreadyExists, result.Error?.Code);
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(temp.Path, "file.txt")));
    }

    [Fact]
    public async Task Write_creates_parent_directories_only_when_requested()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var missingParent = await workspace.WriteFileAsync("scripts/job.js", "x"u8.ToArray());
        var created = await workspace.WriteFileAsync(
            "scripts/job.js",
            "x"u8.ToArray(),
            new ScriptWorkspaceWriteOptions { CreateDirectories = true });

        Assert.False(missingParent.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.NotFound, missingParent.Error?.Code);
        Assert.True(created.Success);
        Assert.True(File.Exists(Path.Combine(temp.Path, "scripts", "job.js")));
    }

    [Fact]
    public async Task Read_only_workspace_returns_read_only_for_writes()
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(
            temp.Path,
            new ScriptFileSystemWorkspaceOptions { IsReadOnly = true });

        var result = await workspace.WriteFileAsync("file.txt", "content"u8.ToArray());

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.ReadOnly, result.Error?.Code);
        Assert.False(File.Exists(Path.Combine(temp.Path, "file.txt")));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute/path.txt")]
    [InlineData(@"C:\absolute\path.txt")]
    [InlineData("https://example.test/file.txt")]
    public async Task Unsafe_paths_return_invalid_path(string path)
    {
        using var temp = TemporaryDirectory.Create();
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var read = await workspace.ReadFileAsync(path);
        var write = await workspace.WriteFileAsync(path, "content"u8.ToArray());

        Assert.False(read.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, read.Error?.Code);
        Assert.False(write.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, write.Error?.Code);
    }

    [Fact]
    public async Task Sandbox_escape_attempts_do_not_create_outside_files()
    {
        using var temp = TemporaryDirectory.Create();
        var outsidePath = Path.Combine(temp.ParentPath, "outside.txt");
        File.Delete(outsidePath);
        var workspace = new ScriptFileSystemWorkspace(temp.Path);

        var result = await workspace.WriteFileAsync("../outside.txt", "outside"u8.ToArray());

        Assert.False(result.Success);
        Assert.Equal(ScriptWorkspaceErrorCode.InvalidPath, result.Error?.Code);
        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task Reparse_point_paths_return_access_denied_when_denied()
    {
        using var temp = TemporaryDirectory.Create();
        var target = Path.Combine(temp.ParentPath, $"{Path.GetFileName(temp.Path)}-target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "secret.txt"), "secret");
        var link = Path.Combine(temp.Path, "link");
        if (!TryCreateDirectorySymbolicLink(link, target))
        {
            Directory.Delete(target, recursive: true);
            return;
        }

        try
        {
            var workspace = new ScriptFileSystemWorkspace(temp.Path);

            var result = await workspace.ReadFileAsync("link/secret.txt");

            Assert.False(result.Success);
            Assert.Equal(ScriptWorkspaceErrorCode.AccessDenied, result.Error?.Code);
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void Reparse_point_root_is_rejected_when_denied()
    {
        using var temp = TemporaryDirectory.Create();
        var target = Path.Combine(temp.ParentPath, $"{Path.GetFileName(temp.Path)}-root-target");
        var link = Path.Combine(temp.ParentPath, $"{Path.GetFileName(temp.Path)}-root-link");
        Directory.CreateDirectory(target);
        if (!TryCreateDirectorySymbolicLink(link, target))
        {
            Directory.Delete(target, recursive: true);
            return;
        }

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => new ScriptFileSystemWorkspace(link));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(target, recursive: true);
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
            ParentPath = System.IO.Path.GetDirectoryName(path)!;
        }

        public string Path { get; }

        public string ParentPath { get; }

        public static TemporaryDirectory Create()
        {
            var directory = CreateMissing();
            Directory.CreateDirectory(directory.Path);
            return directory;
        }

        public static TemporaryDirectory CreateMissing()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"guida-scripting-{Guid.NewGuid():N}");
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
