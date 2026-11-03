namespace Portal.Services;

public static partial class Updater
{
    public static void CleanupAfterUpdate() { }
    public static Task<UpdateCheck> CheckAsync(CancellationToken ct) => Task.FromResult<UpdateCheck>(new UpdateCheck.UpToDate());
    public static Task StageAsync(IProgress<double> progress, CancellationToken ct) => Task.CompletedTask;
    public static void Restart() { }
}
