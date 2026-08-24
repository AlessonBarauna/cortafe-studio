using CortaFeStudio.Api.Models;
using CortaFeStudio.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CortaFeStudio.Tests;

public sealed class ProductionPipelineTests
{
    [Fact]
    public void EnsureStages_CriaFluxoCompletoNaOrdemProfissional()
    {
        var batch = new ProductionBatch();
        ProductionPipeline.EnsureStages(batch);

        Assert.Equal(15, batch.PipelineStages.Count);
        Assert.Equal(ProductionStageName.Acquire, batch.PipelineStages[0].Name);
        Assert.Equal(ProductionStageName.QualityGate, batch.PipelineStages[12].Name);
        Assert.Equal(ProductionStageName.Publish, batch.PipelineStages[^1].Name);
        Assert.All(batch.PipelineStages, stage => Assert.Equal(ProductionStageStatus.Pending, stage.Status));
    }

    [Fact]
    public void EnsureStages_PreservaCheckpointExistenteECompletaModeloAntigo()
    {
        var batch = new ProductionBatch { PipelineStages = [new ProductionStageState { Name = ProductionStageName.Acquire, Status = ProductionStageStatus.Completed, Attempts = 1 }] };
        ProductionPipeline.EnsureStages(batch);

        Assert.Equal(15, batch.PipelineStages.Count);
        var acquire = batch.PipelineStages.Single(stage => stage.Name == ProductionStageName.Acquire);
        Assert.Equal(ProductionStageStatus.Completed, acquire.Status);
        Assert.Equal(1, acquire.Attempts);
    }

    [Fact]
    public void StageOrder_IncluiTodasAsEtapasSemDuplicidade()
    {
        Assert.Equal(Enum.GetValues<ProductionStageName>(), ProductionPipeline.StageOrder);
        Assert.Equal(ProductionPipeline.StageOrder.Length, ProductionPipeline.StageOrder.Distinct().Count());
    }

    [Fact]
    public async Task WorkLimiter_RespeitaLimiteConfiguradoSemCriarThreadsManuais()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ProductionConcurrency:Render"] = "1" }).Build();
        var limiter = new ProductionWorkLimiter(configuration); using var first = await limiter.EnterAsync(ProductionWorkKind.Render);
        var second = limiter.EnterAsync(ProductionWorkKind.Render); await Task.Delay(50);
        Assert.False(second.IsCompleted);
        first.Dispose(); using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(acquired);
    }
}
