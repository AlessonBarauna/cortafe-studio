using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CortaFeStudio.Api.Models;

namespace CortaFeStudio.Api.Services;

public static class ShortFormMetadataService
{
    private static readonly HashSet<string> GenericSpamHashtags = new(StringComparer.OrdinalIgnoreCase)
    {
        "#fyp",
        "#fy",
        "#foryou",
        "#foryoupage",
        "#viral",
        "#parati",
        "#trend",
        "#trending"
    };

    private static readonly Dictionary<string, string[]> ProfileHashtags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pregacao"] = ["#fe", "#jesus", "#pregacao", "#vidacrista", "#reflexao"],
        ["louvor"] = ["#louvor", "#adoracao", "#worship", "#fe", "#musicaCrista"],
        ["podcast"] = ["#podcast", "#cortes", "#entrevista", "#conversa", "#insights"],
        ["aula"] = ["#aprendizado", "#educacao", "#estudo", "#conhecimento", "#dicas"],
        ["motivacao"] = ["#motivacao", "#desenvolvimentoPessoal", "#mentalidade", "#disciplina", "#reflexao"],
        ["negocios"] = ["#negocios", "#marketing", "#empreendedorismo", "#vendas", "#gestao"],
        ["tecnologia"] = ["#tecnologia", "#programacao", "#dev", "#software", "#inovacao"]
    };

    public static async Task EnrichAsync(
        IHttpClientFactory http,
        ClipCandidate clip,
        string contentType,
        CancellationToken ct)
    {
        ApplyFallbacks(clip, contentType);

        try
        {
            var prompt = BuildPrompt(clip, contentType);
            var payload = JsonSerializer.Serialize(new
            {
                model = "qwen2.5:3b",
                prompt,
                stream = false,
                format = "json"
            });

            using var content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/json");

            using var response = await http
                .CreateClient()
                .PostAsync(
                    "http://localhost:11434/api/generate",
                    content,
                    ct);

            if (!response.IsSuccessStatusCode)
                return;

            using var outer = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(ct));

            var raw = outer.RootElement
                .GetProperty("response")
                .GetString();

            if (string.IsNullOrWhiteSpace(raw))
                return;

            using var generated = JsonDocument.Parse(raw);
            ApplyGenerated(
                clip,
                contentType,
                generated.RootElement);
        }
        catch
        {
            // O metadata heurístico continua disponível quando o Ollama não responde.
        }
        finally
        {
            ApplyPlatformMetadata(clip, contentType);
        }
    }

    public static IReadOnlyList<string> GenerateTitleSuggestions(ClipCandidate clip, string contentType, IEnumerable<string>? existing = null)
    {
        var cleaned = Clean(Regex.Replace(clip.Transcript ?? "", @"\[(música|music)\]", "", RegexOptions.IgnoreCase));
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidates = Regex.Split(cleaned, @"(?<=[.!?])\s+|\s*[|\n]\s*").Where(value => value.Length > 0).ToList();
        foreach (var offset in new[] { 0, Math.Max(0, words.Length / 2 - 4), Math.Max(0, words.Length - 9) })
            if (words.Length > 0) candidates.Add(string.Join(' ', words.Skip(offset).Take(contentType == "louvor" ? 8 : 11)));
        if (!string.IsNullOrWhiteSpace(clip.HookSentence)) candidates.Insert(0, clip.HookSentence);
        var blocked = (existing ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        var result = candidates.Select(NormalizeTitle).Where(title => title.Length >= 12 && !IsGenericTitle(title))
            .OrderByDescending(title => TitleScore(title, contentType)).Where(title => !blocked.Any(other => Similar(title, other)))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        foreach (var fallback in BuildFallbackVariants(cleaned, contentType))
            if (result.Count < 3 && !result.Any(title => Similar(title, fallback)) && !blocked.Any(title => Similar(title, fallback))) result.Add(fallback);
        return result.Take(3).ToList();
    }

    public static void EnsureUniqueTitles(IList<ClipCandidate> clips, string contentType)
    {
        var accepted = new List<string>();
        foreach (var clip in clips.OrderBy(clip => clip.Start))
        {
            if (!clip.TitleEditedByUser && (IsGenericTitle(clip.Title) || accepted.Any(title => Similar(title, clip.Title))))
                clip.Title = GenerateTitleSuggestions(clip, contentType, accepted).FirstOrDefault() ?? clip.Title;
            accepted.Add(clip.Title);
        }
    }

    public static void ApplyPlatformMetadata(ClipCandidate clip, string contentType)
    {
        ApplyFallbacks(clip, contentType);
        var tags = NormalizeHashtags(clip.Hashtags, contentType);
        if (contentType is "pregacao" or "louvor" && !tags.Contains("#AmadoJesus", StringComparer.OrdinalIgnoreCase))
            tags.Insert(Math.Min(3, tags.Count), "#AmadoJesus");
        var nicheTags = tags.Take(5).ToList();
        var youtubeTitle = NormalizeTitle(clip.Title);
        var hookOptions = BuildHookOptions(clip, contentType);
        var recommendedHook = hookOptions.FirstOrDefault() ?? youtubeTitle;
        var youtubeCta = CallToAction(contentType, "youtube");
        var instagramFirstLine = recommendedHook;
        var instagramCta = CallToAction(contentType, "instagram");
        var tiktokCta = CallToAction(contentType, "tiktok");
        var value = NormalizeCaption(clip.Caption);
        var warnings = CopyWarnings(recommendedHook, value, nicheTags);
        var copyScore = Math.Round((double)Math.Clamp(100 - warnings.Count * 12 + (recommendedHook.Length is >= 22 and <= 90 ? 4 : 0) + (nicheTags.Count is >= 4 and <= 6 ? 4 : 0), 0, 100), 0);
        clip.PlatformMetadata = new PlatformMetadata
        {
            YouTube = new YouTubeMetadata { Title = youtubeTitle[..Math.Min(100, youtubeTitle.Length)], Description = NormalizeCaption($"{recommendedHook}\n\n{value}\n\n{youtubeCta}\n\n{string.Join(' ', nicheTags)}"), Hashtags = nicheTags, CallToAction = youtubeCta },
            Instagram = new InstagramMetadata { FirstLine = instagramFirstLine, Caption = NormalizeCaption($"{instagramFirstLine}\n\n{value}\n\n{instagramCta}"), Hashtags = nicheTags, CallToAction = instagramCta },
            TikTok = new TikTokMetadata { Caption = NormalizeCaption($"{recommendedHook}\n\n{value}\n\n{tiktokCta}"), Hashtags = nicheTags.Take(5).ToList(), CallToAction = tiktokCta },
            RecommendedHook = recommendedHook,
            HookOptions = hookOptions,
            CopyScore = copyScore,
            CopyWarnings = warnings,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public static List<string> BuildHookOptions(ClipCandidate clip, string contentType)
    {
        var options = new List<string>();
        Add(clip.HookSentence);
        Add(clip.Title);
        var sentences = Regex.Split(Clean(clip.Transcript), @"(?<=[.!?])\s+")
            .Select(NormalizeTitle).Where(value => value.Length is >= 18 and <= 100).ToList();
        foreach (var sentence in sentences.Where(value => value.Contains('?') || Regex.IsMatch(value, @"\b(mas|porém|porque|nunca|quando|verdade|segredo|erro)\b", RegexOptions.IgnoreCase))) Add(sentence);
        foreach (var sentence in sentences) Add(sentence);
        if (options.Count < 3 && !string.IsNullOrWhiteSpace(clip.CoverText)) Add(clip.CoverText);
        return options.Take(3).ToList();

        void Add(string? value)
        {
            var normalized = NormalizeTitle(value);
            if (normalized.Length < 12 || IsGenericTitle(normalized) || options.Any(existing => Similar(existing, normalized))) return;
            options.Add(normalized);
        }
    }

    private static string CallToAction(string contentType, string platform) => (contentType, platform) switch
    {
        ("louvor", _) => "Salve para ouvir novamente e envie para alguém que precisa desta canção.",
        ("pregacao", "tiktok") => "Qual frase falou com você? Escreva nos comentários.",
        ("pregacao", _) => "Compartilhe com alguém que precisa ouvir esta mensagem.",
        ("podcast", _) => "Você concorda com esse ponto? Conte sua leitura nos comentários.",
        ("aula", _) => "Salve para revisar e aplicar depois.",
        ("negocios", _) => "Qual dessas ideias você aplicaria primeiro?",
        ("tecnologia", _) => "Salve como referência e compartilhe com quem trabalha com tecnologia.",
        _ => "Salve este trecho e compartilhe com quem precisa desta ideia."
    };

    private static List<string> CopyWarnings(string hook, string caption, IReadOnlyCollection<string> hashtags)
    {
        var warnings = new List<string>();
        if (hook.Length < 18) warnings.Add("Gancho curto demais para contextualizar o corte.");
        if (hook.Length > 100) warnings.Add("Gancho longo demais para leitura imediata.");
        if (caption.Length < 45) warnings.Add("Descrição precisa explicar melhor o valor do trecho.");
        if (hashtags.Count < 4) warnings.Add("Poucas hashtags específicas do tema.");
        if (Regex.IsMatch(hook, @"você precisa (ver|ouvir)|imperdível|chocante", RegexOptions.IgnoreCase)) warnings.Add("Gancho com sinal de clickbait genérico.");
        return warnings;
    }

    public static (string Title, string Description) ForPlatform(ClipCandidate clip, SocialPlatform platform) => platform switch
    {
        SocialPlatform.YouTube => (clip.PlatformMetadata.YouTube.Title, clip.PlatformMetadata.YouTube.Description),
        SocialPlatform.Instagram => (clip.PlatformMetadata.Instagram.FirstLine, clip.PlatformMetadata.Instagram.Caption + "\n\n" + string.Join(' ', clip.PlatformMetadata.Instagram.Hashtags)),
        _ => (clip.Title, clip.PlatformMetadata.TikTok.Caption + "\n\n" + string.Join(' ', clip.PlatformMetadata.TikTok.Hashtags))
    };

    public static string NormalizeTitle(string? value)
    {
        var text = Clean(value)
            .Trim('"', '\'', ' ', '.', '-', '—');

        if (text.Length <= 75)
            return text;

        var shortened = text[..75];
        var lastSpace = shortened.LastIndexOf(' ');
        if (lastSpace >= 45)
            shortened = shortened[..lastSpace];

        return shortened.TrimEnd(',', ':', ';', '-', '—') + "…";
    }

    public static string NormalizeCoverText(string? value)
    {
        var words = Clean(value)
            .ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(6);

        return string.Join(' ', words)
            .Trim(' ', ',', '.', '?', '!', ':', ';', '-', '—');
    }

    public static string NormalizeCaption(string? value)
    {
        var text = (value ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();

        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        if (text.Length <= 700)
            return text;

        var shortened = text[..700];
        var lastBreak = Math.Max(
            shortened.LastIndexOf('.'),
            shortened.LastIndexOf('?'));

        if (lastBreak >= 420)
            shortened = shortened[..(lastBreak + 1)];

        return shortened.Trim();
    }

    public static List<string> NormalizeHashtags(
        IEnumerable<string>? values,
        string contentType)
    {
        var result = new List<string>();

        foreach (var raw in values ?? [])
        {
            var tag = NormalizeHashtag(raw);
            if (tag is null || GenericSpamHashtags.Contains(tag))
                continue;

            if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase))
                result.Add(tag);

            if (result.Count == 7)
                break;
        }

        var defaults = ProfileHashtags.TryGetValue(contentType, out var profile)
            ? profile
            : ["#cortes", "#conteudo", "#reflexao", "#video"];

        foreach (var item in defaults)
        {
            if (result.Count >= 5)
                break;

            var tag = NormalizeHashtag(item);
            if (tag is not null &&
                !result.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(tag);
            }
        }

        return result.Take(7).ToList();
    }

    private static string BuildPrompt(
        ClipCandidate clip,
        string contentType)
    {
        var profile = contentType switch
        {
            "pregacao" => "pregação cristã e reflexão bíblica",
            "louvor" => "louvor, adoração e música cristã",
            "podcast" => "podcast e entrevista",
            "aula" => "educação e conteúdo explicativo",
            "motivacao" => "motivação e desenvolvimento pessoal",
            "negocios" => "negócios, marketing e vendas",
            "tecnologia" => "tecnologia e programação",
            _ => contentType
        };

        return $$"""
Você é um editor sênior de vídeos curtos para TikTok, Instagram Reels e YouTube Shorts.
O conteúdo é de {{profile}}.

Sua tarefa é transformar a transcrição abaixo em metadata pronta para publicação.
Responda SOMENTE JSON válido, sem markdown, exatamente com:
{
  "title": "...",
  "coverText": "...",
  "caption": "...",
  "hashtags": ["#...", "#..."]
}

REGRAS DO TÍTULO:
- Português do Brasil.
- Entre 35 e 75 caracteres sempre que possível.
- Precisa criar curiosidade sobre uma ideia CONCRETA realmente presente no trecho.
- Pode usar pergunta, contraste, alerta, descoberta ou afirmação forte.
- Não usar clickbait falso.
- Não prometer algo que o vídeo não entrega.
- Evitar títulos genéricos como "Mensagem poderosa", "Momento incrível", "Você precisa ouvir isso" ou "Reflexão do dia".
- Não escrever o título inteiro em maiúsculas.

REGRAS DA CAPA:
- 3 a 6 palavras.
- Leitura instantânea em tela de celular.
- Pode ser mais forte que o título, mas deve continuar fiel ao trecho.
- Sem emoji.

REGRAS DA LEGENDA:
- 2 a 4 blocos curtos.
- A primeira linha deve prender atenção sem repetir mecanicamente o título.
- Explicar por que vale assistir sem entregar toda a conclusão.
- Finalizar com uma pergunta ou CTA natural para comentário/compartilhamento quando fizer sentido.
- Não colocar hashtags dentro da legenda.
- Não usar frases artificiais como "curta, compartilhe e siga" em todo vídeo.

REGRAS DAS HASHTAGS:
- Gerar de 4 a 7.
- Misturar tema específico + nicho.
- Priorizar relevância ao trecho.
- Não usar #fyp, #fy, #foryou, #viral, #parati, #trend ou variações genéricas de spam.

Gancho identificado, se houver:
{{clip.HookSentence}}

Título editorial atual:
{{clip.Title}}

Transcrição do corte:
{{clip.Transcript}}
""";
    }

    private static void ApplyGenerated(
        ClipCandidate clip,
        string contentType,
        JsonElement root)
    {
        if (!clip.TitleEditedByUser && root.TryGetProperty("title", out var title))
        {
            var normalized = NormalizeTitle(title.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
                clip.Title = normalized;
        }

        if (root.TryGetProperty("coverText", out var cover))
        {
            var normalized = NormalizeCoverText(cover.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
                clip.CoverText = normalized;
        }

        if (root.TryGetProperty("caption", out var caption))
        {
            var normalized = NormalizeCaption(caption.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
                clip.Caption = normalized;
        }

        IEnumerable<string> tags = [];
        if (root.TryGetProperty("hashtags", out var hashtags) &&
            hashtags.ValueKind == JsonValueKind.Array)
        {
            tags = hashtags
                .EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>();
        }

        clip.Hashtags = NormalizeHashtags(tags, contentType);
        ApplyFallbacks(clip, contentType);
    }

    private static void ApplyFallbacks(
        ClipCandidate clip,
        string contentType)
    {
        clip.Title = NormalizeTitle(clip.Title);
        clip.CoverText = NormalizeCoverText(clip.CoverText);
        clip.Caption = NormalizeCaption(clip.Caption);
        clip.Hashtags = NormalizeHashtags(clip.Hashtags, contentType);

        if (string.IsNullOrWhiteSpace(clip.Title))
        {
            clip.Title = BuildFallbackTitle(clip.Transcript);
        }

        if (string.IsNullOrWhiteSpace(clip.CoverText))
        {
            clip.CoverText = NormalizeCoverText(clip.Title);
        }

        if (string.IsNullOrWhiteSpace(clip.Caption))
        {
            clip.Caption = $"{clip.Title}\n\nQual parte desse trecho mais chamou sua atenção?";
        }
    }

    private static string BuildFallbackTitle(string transcript)
    {
        var cleaned = Clean(transcript)
            .Trim(' ', ',', '.', '?', '!', ':', ';', '-', '—');

        if (string.IsNullOrWhiteSpace(cleaned))
            return "Uma ideia que merece ser pensada com calma";

        var sentence = Regex.Split(cleaned, @"(?<=[.!?])\s+")
            .FirstOrDefault(part => part.Length >= 25)
            ?? cleaned;

        var words = sentence
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(12);

        return NormalizeTitle(string.Join(' ', words));
    }

    private static IEnumerable<string> BuildFallbackVariants(string text, string contentType)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) yield break;
        yield return NormalizeTitle(string.Join(' ', words.Take(contentType == "louvor" ? 7 : 10)));
        if (words.Length > 7) yield return NormalizeTitle(string.Join(' ', words.Skip(words.Length / 3).Take(8)));
        if (words.Length > 12) yield return NormalizeTitle(string.Join(' ', words.Skip(words.Length * 2 / 3).Take(8)));
    }

    private static bool IsGenericTitle(string? title)
    {
        var folded = Clean(title).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(folded) || new[] { "momento de louvor", "trecho do louvor", "mensagem para você", "promessa maior" }.Any(folded.Contains);
    }

    private static double TitleScore(string title, string contentType)
    {
        var score = title.Length is >= 20 and <= 65 ? 20 : 5;
        if (contentType == "louvor" && new[] { "fiel", "promessa", "creio", "deus", "jesus", "aqui", "sempre", "amor" }.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase))) score += 15;
        return score + title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    private static bool Similar(string left, string right)
    {
        var a = Regex.Matches(Clean(left).ToLowerInvariant(), @"[\p{L}\p{Nd}]+").Select(match => match.Value).ToHashSet();
        var b = Regex.Matches(Clean(right).ToLowerInvariant(), @"[\p{L}\p{Nd}]+").Select(match => match.Value).ToHashSet();
        if (a.Count == 0 || b.Count == 0) return false;
        return a.Intersect(b).Count() / (double)a.Union(b).Count() >= .72;
    }

    private static string? NormalizeHashtag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim();
        if (!cleaned.StartsWith('#'))
            cleaned = "#" + cleaned;

        cleaned = Regex.Replace(
            cleaned,
            @"[^#\p{L}\p{Nd}_]",
            "");

        if (cleaned.Length <= 1)
            return null;

        return cleaned;
    }

    private static string Clean(string? value) =>
        Regex.Replace(
            (value ?? "")
                .Replace('\n', ' ')
                .Replace('\r', ' '),
            @"\s+",
            " ")
        .Trim();
}
