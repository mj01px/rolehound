using Rolehound.Api.Infrastructure.Email;

namespace Rolehound.Api.Tests.Accounts;

public sealed class EmailLayoutTests
{
    [Fact]
    public void An_email_has_html_and_a_text_copy_and_encodes_what_it_shows()
    {
        var message = EmailLayout.Render("ana@exemplo.test", "Assunto", new EmailContent(
            "Prévia",
            "Olá, <Ana>",
            ["Texto com <script>alert(1)</script>"],
            new EmailAction("Abrir", new Uri("https://app.test/acesso#token=a&b")),
            Code: "123456",
            Note: "Vale por 1 hora."));

        Assert.NotNull(message.Html);
        Assert.Contains("Olá, &lt;Ana&gt;", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", message.Html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://app.test/acesso#token=a&amp;b\"", message.Html, StringComparison.Ordinal);
        Assert.Contains("123456", message.Html, StringComparison.Ordinal);
        Assert.Contains("https://app.test/acesso#token=a&b", message.Body, StringComparison.Ordinal);
        Assert.Contains("Vale por 1 hora.", message.Body, StringComparison.Ordinal);
    }
}
