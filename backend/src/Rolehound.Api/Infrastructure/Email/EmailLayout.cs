using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Rolehound.Api.Infrastructure.Email;

/// <summary>A button in an email: one per message, the thing to do.</summary>
public sealed record EmailAction(string Label, Uri Url);

/// <summary>
/// What an email says. <see cref="Preheader"/> is the line inboxes show next to the subject;
/// <see cref="Code"/> is a one-time code shown large; <see cref="Note"/> is the small print under the action.
/// </summary>
public sealed record EmailContent(
    string Preheader,
    string Title,
    IReadOnlyList<string> Paragraphs,
    EmailAction? Action = null,
    string? Code = null,
    string? Note = null);

/// <summary>
/// The look of every Rolehound email, in the app's own colors (dark ground, one white button,
/// green only in the logo's cursor). Built from tables with inline styles, which is what email
/// clients render reliably; no images, so nothing has to load. Every value is HTML-encoded.
/// A plain-text version goes along for clients that do not show HTML.
/// </summary>
public static class EmailLayout
{
    private const string Sans = "'Geist', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";
    private const string Mono = "'Geist Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
    private const string Bg = "#09090b";
    private const string Surface = "#121215";
    private const string Raised = "#18181b";
    private const string Border = "#27272a";
    private const string Ink = "#fafafa";
    private const string Muted = "#a1a1aa";
    private const string Faint = "#8b8b94";
    private const string Signal = "#10b981";

    private const string Footer = "Se não foi você, ignore este e-mail ou avise o administrador.";

    // Encodes markup characters but keeps accented letters as they are, so the source stays readable.
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Create(UnicodeRanges.All);

    public static EmailMessage Render(string to, string subject, EmailContent content) =>
        new(to, subject, Text(content), Html(subject, content));

    private static string Text(EmailContent c)
    {
        var text = new StringBuilder();
        text.Append(c.Title).Append("\n\n");
        foreach (var paragraph in c.Paragraphs)
        {
            text.Append(paragraph).Append("\n\n");
        }

        if (c.Code is not null)
        {
            text.Append(c.Code).Append("\n\n");
        }

        if (c.Action is not null)
        {
            text.Append(c.Action.Label).Append(":\n").Append(c.Action.Url).Append("\n\n");
        }

        if (c.Note is not null)
        {
            text.Append(c.Note).Append("\n\n");
        }

        return text.Append(Footer).Append("\n\nrolehound").ToString();
    }

    private static string Html(string subject, EmailContent c)
    {
        static string E(string value) => Encoder.Encode(value);

        var body = new StringBuilder();
        foreach (var paragraph in c.Paragraphs)
        {
            body.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 16px;font:400 15px/24px {Sans};color:{Muted};">{E(paragraph)}</p>""");
        }

        if (c.Code is not null)
        {
            body.Append(CultureInfo.InvariantCulture, $"""
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px 0 24px;">
                  <tr><td bgcolor="{Raised}" style="background:{Raised};border:1px solid {Border};border-radius:8px;padding:16px 24px;font:600 30px/36px {Mono};letter-spacing:8px;color:{Ink};">{E(c.Code)}</td></tr>
                </table>
                """);
        }

        if (c.Action is not null)
        {
            var url = E(c.Action.Url.AbsoluteUri);
            body.Append(CultureInfo.InvariantCulture, $"""
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px 0 24px;">
                  <tr><td bgcolor="{Ink}" style="background:{Ink};border-radius:6px;">
                    <a href="{url}" target="_blank" style="display:inline-block;padding:13px 22px;font:600 15px/20px {Sans};color:{Bg};text-decoration:none;border-radius:6px;">{E(c.Action.Label)}</a>
                  </td></tr>
                </table>
                <p style="margin:0 0 16px;font:400 13px/20px {Sans};color:{Faint};">Se o botão não abrir, copie este endereço no navegador:<br><a href="{url}" target="_blank" style="color:{Muted};word-break:break-all;">{url}</a></p>
                """);
        }

        if (c.Note is not null)
        {
            body.Append(CultureInfo.InvariantCulture, $"""<p style="margin:0 0 4px;font:400 13px/20px {Sans};color:{Faint};">{E(c.Note)}</p>""");
        }

        return $"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="dark">
            <meta name="supported-color-schemes" content="dark">
            <title>{E(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background:{Bg};">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:{Bg};">{E(c.Preheader)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="{Bg}" style="background:{Bg};">
              <tr><td align="center" style="padding:40px 16px;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">
                  <tr><td style="padding:0 4px 24px;">
                    <span style="font:600 20px/24px {Sans};letter-spacing:-0.6px;color:{Ink};">rolehound</span><span style="display:inline-block;width:9px;height:16px;margin-left:3px;background:{Signal};vertical-align:-1px;"></span>
                  </td></tr>
                  <tr><td bgcolor="{Surface}" style="background:{Surface};border:1px solid {Border};border-radius:10px;padding:36px 32px 28px;">
                    <h1 style="margin:0 0 16px;font:600 24px/30px {Sans};letter-spacing:-0.5px;color:{Ink};">{E(c.Title)}</h1>
                    {body}
                  </td></tr>
                  <tr><td style="padding:24px 4px 0;font:400 12px/18px {Sans};color:{Faint};">
                    {E(Footer)}<br>rolehound · acesso só por convite
                  </td></tr>
                </table>
              </td></tr>
            </table>
            </body>
            </html>
            """;
    }
}
