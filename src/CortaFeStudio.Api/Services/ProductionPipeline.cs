using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ProductionPipeline(ProductionBatchService batches, SocialService social, ILogger<ProductionPipeline> logger)
{
    public static readonly ProductionStageName[] StageOrder = Enum.GetValues<ProductionStageName>();

    public async Task RunPendingAsync(CancellationToken ct)
    {
        foreach (var batch in batches.Pending())
        {
            ct.ThrowIfCancellationRequested(); EnsureStages(batch); var project = batches.ProjectFor(batch);
            try
            {
                var before = Snapshot(batch); Synchronize(batch, project); LogTransitions(batch, before);
                if (batch.Status is ProductionStatus.Queued or ProductionStatus.Analyzing) await batches.AdvanceAsync(batch, ct);
                before = Snapshot(batch); Synchronize(batch, batches.ProjectFor(batch)); LogTransitions(batch, before); await batches.SaveBatchAsync(batch);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var stage = Current(batch); stage.Status = ProductionStageStatus.Failed; stage.Error = ex.Message; stage.CompletedAt = DateTime.UtcNow;
                batch.Status = ProductionStatus.Failed; batch.Error = ex.Message; batch.Stage = $"Falha em {stage.Name}"; await batches.SaveBatchAsync(batch);
                logger.LogError(ex, "[Production] batch={BatchId} stage={Stage} attempt={Attempt}", batch.Id, stage.Name, stage.Attempts);
            }
        }
    }

    public static void EnsureStages(ProductionBatch batch)
    {
        foreach (var name in StageOrder) if (batch.PipelineStages.All(stage => stage.Name != name)) batch.PipelineStages.Add(new ProductionStageState { Name = name });
        batch.PipelineStages = batch.PipelineStages.OrderBy(stage => Array.IndexOf(StageOrder, stage.Name)).ToList();
    }

    private void Synchronize(ProductionBatch batch, VideoProject? project)
    {
        if (project is null) return;
        CompleteIf(batch, ProductionStageName.Acquire, project.CompletedStages.Contains("media"));
        CompleteIf(batch, ProductionStageName.Transcribe, project.CompletedStages.Contains("transcript"));
        foreach (var name in new[] { ProductionStageName.Analyze, ProductionStageName.GenerateCandidates, ProductionStageName.RefineHooks, ProductionStageName.CalculateSocialScores }) CompleteIf(batch, name, project.CompletedStages.Contains("analysis") && project.Clips.Count > 0);
        CompleteIf(batch, ProductionStageName.GenerateVariants, batch.Items.Count > 0 && batch.Items.All(item => project.Clips.First(clip => clip.Id == item.ClipId).Variants.Count >= batch.Settings.VariantCount));
        CompleteIf(batch, ProductionStageName.SelectWinners, batch.Items.Count > 0 && batch.Items.All(item => project.Clips.First(clip => clip.Id == item.ClipId).WinningVariantId is not null));
        CompleteIf(batch, ProductionStageName.GenerateMetadata, batch.Items.Count > 0 && batch.Items.All(item => !string.IsNullOrWhiteSpace(project.Clips.First(clip => clip.Id == item.ClipId).PlatformMetadata.YouTube.Title)));
        var rendered = batch.Items.Count > 0 && batch.Items.Where(item => item.Approved).All(item => item.Rendered);
        if (!batch.Settings.AutoRender) { Skip(batch, ProductionStageName.AnalyzeAudio); Skip(batch, ProductionStageName.AnalyzeVideo); Skip(batch, ProductionStageName.Render); Skip(batch, ProductionStageName.QualityGate); }
        else { CompleteIf(batch, ProductionStageName.AnalyzeAudio, rendered); CompleteIf(batch, ProductionStageName.AnalyzeVideo, rendered); CompleteIf(batch, ProductionStageName.Render, rendered); CompleteIf(batch, ProductionStageName.QualityGate, rendered && batch.Items.Where(item => item.Approved).All(item => item.QualityStatus is QualityStatus.Pass or QualityStatus.Warning)); }
        var publications = batch.Items.SelectMany(item => item.Publications).ToList(); CompleteIf(batch, ProductionStageName.Schedule, publications.Count > 0);
        if (!batch.Settings.AutoSchedule || !batch.Settings.AutoPublish) Skip(batch, ProductionStageName.Schedule);
        var history = social.History().ToDictionary(item => item.Id); var published = publications.Count > 0 && publications.All(item => item.PublicationId is not null && history.TryGetValue(item.PublicationId, out var record) && record.Status == "published");
        if (!batch.Settings.AutoPublish) Skip(batch, ProductionStageName.Publish); else CompleteIf(batch, ProductionStageName.Publish, published);
        if (published) { batch.Status = ProductionStatus.Published; batch.Stage = "Todos os conteudos foram publicados"; batch.CompletedAt = DateTime.UtcNow; }
        MarkCurrentRunning(batch, project);
    }

    private static void MarkCurrentRunning(ProductionBatch batch, VideoProject project)
    {
        var target = project.Status switch { ProjectStatus.Acquiring => ProductionStageName.Acquire, ProjectStatus.Transcribing => ProductionStageName.Transcribe, ProjectStatus.Analyzing => ProductionStageName.Analyze, _ when batch.Status == ProductionStatus.Rendering => ProductionStageName.Render, _ when batch.Status == ProductionStatus.QualityCheck => ProductionStageName.QualityGate, _ when batch.Status == ProductionStatus.Scheduled => ProductionStageName.Publish, _ => (ProductionStageName?)null };
        if (target is null) return; var stage = batch.PipelineStages.First(item => item.Name == target); if (stage.Status != ProductionStageStatus.Pending) return; stage.Status = ProductionStageStatus.Running; stage.Attempts++; stage.StartedAt = DateTime.UtcNow;
    }

    private static void CompleteIf(ProductionBatch batch, ProductionStageName name, bool condition) { if (!condition) return; var stage = batch.PipelineStages.First(item => item.Name == name); if (stage.Status == ProductionStageStatus.Completed) return; stage.Status = ProductionStageStatus.Completed; stage.Error = null; stage.CompletedAt = DateTime.UtcNow; batch.LastPipelineCheckpoint = name.ToString(); }
    private static void Skip(ProductionBatch batch, ProductionStageName name) { var stage = batch.PipelineStages.First(item => item.Name == name); if (stage.Status == ProductionStageStatus.Pending) stage.Status = ProductionStageStatus.Skipped; }
    private static ProductionStageState Current(ProductionBatch batch) => batch.PipelineStages.FirstOrDefault(stage => stage.Status == ProductionStageStatus.Running) ?? batch.PipelineStages.First(stage => stage.Status == ProductionStageStatus.Pending);
    private static Dictionary<ProductionStageName, ProductionStageStatus> Snapshot(ProductionBatch batch) => batch.PipelineStages.ToDictionary(stage => stage.Name, stage => stage.Status);
    private void LogTransitions(ProductionBatch batch, IReadOnlyDictionary<ProductionStageName, ProductionStageStatus> before)
    {
        foreach (var stage in batch.PipelineStages.Where(stage => !before.TryGetValue(stage.Name, out var status) || status != stage.Status)) logger.LogInformation("[Production] batch={BatchId} stage={Stage} status={Status} attempt={Attempt}", batch.Id, stage.Name, stage.Status, stage.Attempts);
    }
}
