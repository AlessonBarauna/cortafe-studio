namespace CortaFeStudio.Api.Services;

public enum ProductionWorkKind { Transcription, Render, Metadata, Upload }

public sealed class ProductionConcurrencyOptions
{
    public int Transcription { get; set; } = 1;
    public int Render { get; set; } = Math.Clamp(Environment.ProcessorCount / 4, 1, 2);
    public int Metadata { get; set; } = 2;
    public int Upload { get; set; } = 1;
}

public sealed class ProductionWorkLimiter
{
    private readonly IReadOnlyDictionary<ProductionWorkKind, SemaphoreSlim> _limits;
    public ProductionWorkLimiter(IConfiguration configuration)
    {
        var options = configuration.GetSection("ProductionConcurrency").Get<ProductionConcurrencyOptions>() ?? new();
        _limits = new Dictionary<ProductionWorkKind, SemaphoreSlim>
        {
            [ProductionWorkKind.Transcription] = Limit(options.Transcription, 1),
            [ProductionWorkKind.Render] = Limit(options.Render, 2),
            [ProductionWorkKind.Metadata] = Limit(options.Metadata, 4),
            [ProductionWorkKind.Upload] = Limit(options.Upload, 2)
        };
    }

    public async Task<IDisposable> EnterAsync(ProductionWorkKind kind, CancellationToken ct = default)
    {
        var semaphore = _limits[kind]; await semaphore.WaitAsync(ct); return new Releaser(semaphore);
    }

    private static SemaphoreSlim Limit(int value, int maximum) => new(Math.Clamp(value, 1, maximum), Math.Clamp(value, 1, maximum));
    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable { private int _released; public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release(); } }
}
