using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public sealed class ManualClipService
{
    public ClipCandidate Create(VideoProject project, double start, double end)
    {
        if (project.Status != ProjectStatus.Ready)
            throw new InvalidOperationException("O vídeo precisa terminar de ser processado antes da edição manual.");
        if (string.IsNullOrWhiteSpace(project.LocalMedia))
            throw new InvalidOperationException("A mídia original deste projeto não está mais disponível.");
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
            throw new ArgumentException("Informe um intervalo válido para o corte.");
        if (end - start < 1)
            throw new ArgumentException("O corte manual precisa ter pelo menos 1 segundo.");
        if (project.Duration > 0 && end > project.Duration + .05)
            throw new ArgumentException("O fim do corte ultrapassa a duração do vídeo.");

        start = Math.Round(start, 3);
        end = Math.Round(Math.Min(end, project.Duration > 0 ? project.Duration : end), 3);
        var segments = project.Transcript
            .Where(segment => segment.End > start && segment.Start < end)
            .ToList();
        var words = segments.SelectMany(segment => segment.Words)
            .Where(word => word.End > start && word.Start < end)
            .OrderBy(word => word.Start)
            .Select(word => word.Word.Trim())
            .Where(word => word.Length > 0)
            .ToList();
        var transcript = words.Count > 0
            ? string.Join(' ', words)
            : string.Join(' ', segments.Select(segment => segment.Text.Trim()).Where(text => text.Length > 0));
        var hook = FirstSentence(transcript);
        var clip = new ClipCandidate
        {
            Start = start,
            End = end,
            Source = "manual",
            Transcript = transcript,
            HookSentence = hook,
            Title = ShortFormMetadataService.NormalizeTitle(hook),
            CoverText = ShortFormMetadataService.NormalizeCoverText(hook),
            Caption = string.IsNullOrWhiteSpace(hook) ? "Corte criado manualmente." : $"{hook}\n\nQual parte mais chamou sua atenção?",
            Hashtags = ShortFormMetadataService.NormalizeHashtags([], project.Options.ContentType),
            EditorialProfile = project.Options.ContentType,
            Reasons = ["Intervalo selecionado manualmente no vídeo completo"],
            Score = 0,
            Approved = true
        };
        clip.SocialScore = SocialScoreService.Calculate(clip, project.Options);
        return clip;
    }

    private static string FirstSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Corte selecionado manualmente";
        var stop = text.IndexOfAny(['.', '?', '!']);
        var sentence = stop >= 0 ? text[..(stop + 1)] : text;
        return sentence.Length <= 140 ? sentence : sentence[..140].TrimEnd() + "…";
    }
}
