using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Rolehound.Api.Features.TailoredResume;

/// <summary>Contact lines from the Dossier, filled in code (BR-AI-05).</summary>
public sealed record ResumeContact(string? Email, string? Phone, string? LinkedIn, string? GitHub, string? Website);

/// <summary>
/// The standard template (FR-AI-07): neutral, one column, section titles in the
/// resume's language, text only so ATS parsers read it. Lato ships with QuestPDF.
/// </summary>
public static class ResumePdf
{
    private const string Ink = "#111111";
    private const string Muted = "#555555";
    private const string Rule = "#D4D4D4";

    public static byte[] Render(ResumeDocument resume, ResumeContact contact)
    {
        var pt = resume.Language == "pt";
        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(10).FontColor(Ink).LineHeight(1.3f));

            page.Header().Column(header =>
            {
                header.Item().Text(resume.Name).FontSize(20).SemiBold();
                header.Item().Text(resume.Headline).FontSize(11).FontColor(Muted);
                var line = string.Join("  |  ", new[] { resume.Location, contact.Email, contact.Phone, contact.LinkedIn, contact.GitHub, contact.Website }.Where(v => !string.IsNullOrWhiteSpace(v)));
                if (line.Length > 0)
                {
                    header.Item().PaddingTop(4).Text(line).FontSize(9).FontColor(Muted);
                }
            });

            page.Content().PaddingTop(12).Column(body =>
            {
                body.Spacing(12);
                Section(body, pt ? "Resumo" : "Summary", c => c.Item().Text(resume.Summary));

                if (resume.Experience.Count > 0)
                {
                    Section(body, pt ? "Experiência" : "Experience", c =>
                    {
                        foreach (var job in resume.Experience)
                        {
                            c.Item().Column(entry =>
                            {
                                entry.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(text =>
                                    {
                                        text.Span(job.Title).SemiBold();
                                        text.Span($", {job.Company}");
                                    });
                                    row.AutoItem().Text(Period(job.Start, job.End, pt)).FontColor(Muted);
                                });
                                if (!string.IsNullOrEmpty(job.Location))
                                {
                                    entry.Item().Text(job.Location).FontSize(9).FontColor(Muted);
                                }

                                foreach (var bullet in job.Bullets)
                                {
                                    entry.Item().PaddingLeft(8).Row(row =>
                                    {
                                        row.ConstantItem(10).Text("•");
                                        row.RelativeItem().Text(bullet);
                                    });
                                }
                            });
                        }
                    });
                }

                if (resume.Projects.Count > 0)
                {
                    Section(body, pt ? "Projetos" : "Projects", c =>
                    {
                        foreach (var project in resume.Projects)
                        {
                            c.Item().Column(entry =>
                            {
                                entry.Item().Text(text =>
                                {
                                    text.Span(project.Name).SemiBold();
                                    if (!string.IsNullOrEmpty(project.Url))
                                    {
                                        text.Span($"  {project.Url}").FontSize(9).FontColor(Muted);
                                    }
                                });
                                entry.Item().Text(project.Description);
                            });
                        }
                    });
                }

                if (resume.Education.Count > 0)
                {
                    Section(body, pt ? "Formação" : "Education", c =>
                    {
                        foreach (var education in resume.Education)
                        {
                            c.Item().Row(row =>
                            {
                                row.RelativeItem().Text(text =>
                                {
                                    text.Span(education.Degree).SemiBold();
                                    text.Span($", {education.Institution}");
                                    if (education.Status != "completed")
                                    {
                                        text.Span($" ({StatusLabel(education.Status, pt)})").FontColor(Muted);
                                    }
                                });
                                row.AutoItem().Text(Period(education.Start, education.End, pt, open: education.Status == "in_progress")).FontColor(Muted);
                            });
                        }
                    });
                }

                Simple(body, pt ? "Habilidades" : "Skills", resume.Skills, inline: true);
                Simple(body, pt ? "Idiomas" : "Languages", resume.Languages, inline: true);
                Simple(body, pt ? "Certificações" : "Certifications", resume.Certifications, inline: false);
                Simple(body, pt ? "Cursos" : "Courses", resume.Courses, inline: false);

                if (resume.Volunteering.Count > 0)
                {
                    Section(body, pt ? "Voluntariado" : "Volunteering", c =>
                    {
                        foreach (var item in resume.Volunteering)
                        {
                            c.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"{item.Role}, {item.Organization}");
                                row.AutoItem().Text(Period(item.Start, item.End, pt)).FontColor(Muted);
                            });
                        }
                    });
                }
            });
        })).GeneratePdf();
    }

    private static void Section(ColumnDescriptor body, string title, Action<ColumnDescriptor> content) =>
        body.Item().Column(section =>
        {
            section.Spacing(6);
            section.Item().BorderBottom(0.5f).BorderColor(Rule).PaddingBottom(2).Text(title.ToUpperInvariant()).FontSize(9).SemiBold().LetterSpacing(0.05f);
            content(section);
        });

    private static void Simple(ColumnDescriptor body, string title, IReadOnlyList<string> items, bool inline)
    {
        if (items.Count == 0)
        {
            return;
        }

        Section(body, title, c =>
        {
            if (inline)
            {
                c.Item().Text(string.Join(", ", items));
                return;
            }

            foreach (var item in items)
            {
                c.Item().Text(item);
            }
        });
    }

    /// <summary>"03/2024 a 05/2025", "03/2024 a atual" or "03/2024 to present".</summary>
    internal static string Period(string? start, string? end, bool pt, bool open = true)
    {
        if (start is null && end is null)
        {
            return string.Empty;
        }

        var to = pt ? "a" : "to";
        var finish = end is not null ? MonthYear(end) : open ? (pt ? "atual" : "present") : null;
        return start is null ? finish ?? string.Empty : finish is null ? MonthYear(start) : $"{MonthYear(start)} {to} {finish}";
    }

    private static string MonthYear(string yearMonth) => yearMonth.Length == 7 ? $"{yearMonth[5..]}/{yearMonth[..4]}" : yearMonth;

    private static string StatusLabel(string status, bool pt) => (status, pt) switch
    {
        ("in_progress", true) => "em andamento",
        ("incomplete", true) => "incompleto",
        ("in_progress", false) => "in progress",
        ("incomplete", false) => "incomplete",
        _ => status,
    };
}
