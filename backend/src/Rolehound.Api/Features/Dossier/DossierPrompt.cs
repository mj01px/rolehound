namespace Rolehound.Api.Features.Dossier;

/// <summary>
/// The prompt a person runs in an external AI to produce their Dossier
/// (FR-DOS-01). The interface is in Portuguese, so the prompt is too.
/// </summary>
public static class DossierPrompt
{
    public static string Text => $$"""
        Você vai me ajudar a montar meu Dossier profissional para o rolehound, um app que busca vagas e gera currículos sem inventar nada.

        Como trabalhar:
        1. Me entreviste, uma pergunta por vez, para cobrir: nome, título profissional, cidade, contatos, resumo, experiências (empresa, cargo, início e fim, conquistas e tecnologias usadas), projetos, formação, habilidades, idiomas, certificações, cursos, voluntariado e preferências (cargos que busco, nível, modelo de trabalho, cidades, o que quero evitar).
        2. Se eu colar meu currículo atual, use como ponto de partida e pergunte só o que faltar.
        3. Nunca invente nada. Se eu não souber uma informação, deixe o campo de fora (quando ele for opcional).
        4. Cargos, formações, título profissional e resumo vão em português (pt) e em inglês (en). Traduza com o nome que o mercado usa, sem enfeitar.
        5. Datas no formato AAAA-MM. Trabalho atual: "end": null.
        6. No fim, responda com um único bloco JSON, sem comentários, seguindo exatamente o schema abaixo (versão {{DossierSchema.CurrentVersion}}).

        Schema:
        {{DossierSchema.Text}}
        """;
}
