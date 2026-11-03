namespace Portal.Tests;

/// <summary>A fresh folder under the system temp directory, deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    /// <summary>Creates the folder.</summary>
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "portal-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>Writes a file below the folder, creating subfolders, and returns its full path.</summary>
    public string Write(string relativePath, string content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    /// <summary>The full path of a file or folder below the folder.</summary>
    public string Combine(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
