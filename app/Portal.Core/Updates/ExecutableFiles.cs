namespace Portal.Core.Updates;

/// <summary>The files beside the running executable that an update stages, swaps and cleans up.</summary>
internal static class ExecutableFiles
{
    public const string StagedSuffix = ".new";
    public const string PreviousSuffix = ".old";

    /// <summary>The executable's full path with <paramref name="suffix"/> appended to its file name.</summary>
    public static string Sibling(string executablePath, string suffix)
    {
        var name = Path.GetFileName(executablePath);
        var directory = Path.GetDirectoryName(executablePath) ?? string.Empty;
        return Path.Combine(directory, (name.Length == 0 ? UpdatePolicy.AssetName : name) + suffix);
    }

    /// <summary>Moves the running executable to <c>.old</c> and the staged file into its place.</summary>
    public static void Swap(string executablePath, string stagedPath)
    {
        var previous = Sibling(executablePath, PreviousSuffix);
        TryDelete(previous);
        try
        {
            File.Move(executablePath, previous);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException($"cannot move {executablePath} aside", e);
        }

        try
        {
            File.Move(stagedPath, executablePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryMove(previous, executablePath);
            throw new UpdateException($"cannot place {executablePath}", e);
        }
    }

    /// <summary>Removes <c>.old</c> and <c>.new</c> left by an earlier update; files still locked stay.</summary>
    public static void CleanupLeftovers(string executablePath)
    {
        TryDelete(Sibling(executablePath, PreviousSuffix));
        TryDelete(Sibling(executablePath, StagedSuffix));
    }

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
