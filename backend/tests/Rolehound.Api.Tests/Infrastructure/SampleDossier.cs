namespace Rolehound.Api.Tests.Infrastructure;

/// <summary>A small, made-up Dossier that satisfies schema version 1.</summary>
internal static class SampleDossier
{
    public const string Valid = """
        {
          "schemaVersion": 1,
          "person": {
            "name": "Ana Lima",
            "headline": { "pt": "Desenvolvedora Backend", "en": "Backend Developer" },
            "location": "São Paulo, SP"
          },
          "experience": [
            {
              "company": "Loja Exemplo",
              "title": { "pt": "Estagiária de Desenvolvimento", "en": "Software Development Intern" },
              "start": "2025-02",
              "end": null,
              "highlights": ["Criei uma API de pedidos em Django."],
              "skills": ["python", "django"]
            }
          ],
          "education": [
            {
              "institution": "Universidade Exemplo",
              "degree": { "pt": "Bacharelado em Ciência da Computação", "en": "Bachelor of Computer Science" },
              "status": "in_progress"
            }
          ],
          "skills": [{ "name": "Python", "category": "language", "level": "intermediate" }],
          "preferences": { "targetRoles": ["Desenvolvedora Backend"], "seniority": ["internship", "junior"], "workModes": ["remote", "hybrid"] }
        }
        """;

    public const string ProfileOptions = """
        {
          "options": [
            {
              "name": "Backend Python",
              "description": "Foco em Python e APIs.",
              "groups": [
                { "name": "Stack principal", "weight": 12, "terms": ["python", "django"], "stack": true, "bodyDampened": false },
                { "name": "Nível", "weight": 10, "terms": ["junior", "estagio"], "stack": false, "bodyDampened": false },
                { "name": "Fora da área", "weight": -15, "terms": ["vendas"], "stack": false, "bodyDampened": true }
              ],
              "minHighlightScore": 25,
              "yearsBands": [{ "minYears": 5, "penalty": -25 }, { "minYears": 3, "penalty": -12 }],
              "searchTerms": ["desenvolvedor python", "backend junior"]
            },
            {
              "name": "Inválida",
              "description": "Sem grupos: deve ser descartada.",
              "groups": [],
              "minHighlightScore": 25,
              "yearsBands": [],
              "searchTerms": ["x"]
            }
          ]
        }
        """;
}
