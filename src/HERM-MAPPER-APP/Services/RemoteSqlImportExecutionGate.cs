namespace HERMMapperApp.Services;

public sealed class RemoteSqlImportExecutionGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
