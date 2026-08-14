namespace CortaFeStudio.Api.Services;

public sealed record EditorialProfileDefinition(string Id, string Label, int MinDuration, int MaxDuration, string[] Signals, string[] Hashtags, string CaptionSuffix);

public static class EditorialProfiles
{
    private static readonly Dictionary<string, EditorialProfileDefinition> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pregacao"] = new("pregacao", "Pregação", 40, 90, ["deus", "jesus", "fé", "graça", "palavra", "propósito", "coração"], ["#pregação", "#fé", "#palavra", "#shorts"], "Uma mensagem para guardar e compartilhar."),
        ["louvor"] = new("louvor", "Louvor", 25, 60, ["adoração", "louvor", "deus", "presença", "promessa"], ["#louvor", "#adoração", "#worship", "#fé"], "Um momento de louvor para renovar a fé."),
        ["podcast"] = new("podcast", "Podcast e entrevista", 35, 90, ["eu percebi", "experiência", "aprendi", "discordo", "ninguém fala", "aconteceu"], ["#podcast", "#entrevista", "#cortes", "#shorts"], "Um trecho que vale continuar discutindo."),
        ["aula"] = new("aula", "Educação e aulas", 30, 75, ["significa", "conceito", "exemplo", "primeiro", "passo", "entenda"], ["#educação", "#aprendizado", "#dica", "#shorts"], "Uma explicação prática para aprender e aplicar."),
        ["motivacao"] = new("motivacao", "Motivação", 20, 50, ["superar", "disciplina", "mudança", "coragem", "desistir", "transformação", "decida"], ["#motivação", "#disciplina", "#mentalidade", "#reels"], "Uma reflexão para transformar atitude em ação."),
        ["negocios"] = new("negocios", "Negócios e marketing", 25, 60, ["resultado", "cliente", "vendas", "estratégia", "lucro", "empresa", "mercado", "liderança"], ["#negócios", "#marketing", "#empreendedorismo", "#vendas"], "Uma estratégia prática para negócios e resultados."),
        ["tecnologia"] = new("tecnologia", "Tecnologia", 30, 90, ["código", "software", "inteligência artificial", "dados", "programação", "ferramenta", "erro", "solução"], ["#tecnologia", "#programação", "#ia", "#tech"], "Uma explicação técnica direta ao ponto.")
    };
    public static EditorialProfileDefinition Get(string? id) => Profiles.GetValueOrDefault(id ?? "pregacao") ?? Profiles["pregacao"];
    public static IReadOnlyCollection<EditorialProfileDefinition> All => Profiles.Values;
}
