using System.IO.Compression;
using System.Text;
using Rolehound.Api.Features.Ats;
using Rolehound.Api.Tests.Infrastructure;

namespace Rolehound.Api.Tests.Ats;

public sealed class PdfLimitTests
{
    [Fact]
    public void A_small_pdf_that_unpacks_to_too_much_is_refused_before_parsing()
    {
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < (ResumeEndpoints.MaxUnpackedBytes / zeros.Length) + 8; i++)
            {
                zlib.Write(zeros);
            }
        }

        var bomb = Encoding.ASCII.GetBytes("%PDF-1.4\n4 0 obj\n<< /Filter /FlateDecode >>\nstream\n")
            .Concat(packed.ToArray())
            .Concat(Encoding.ASCII.GetBytes("\nendstream\nendobj\n%%EOF\n"))
            .ToArray();

        Assert.True(bomb.Length < ResumeEndpoints.MaxBytes);
        Assert.False(ResumeEndpoints.UnpacksWithinLimit(bomb));
        Assert.NotNull(ResumeEndpoints.Extract(bomb).Error);
    }

    [Fact]
    public void An_ordinary_pdf_passes_the_unpack_check() =>
        Assert.True(ResumeEndpoints.UnpacksWithinLimit(TinyPdf.WithText("Ana Souza", "Desenvolvedora Python há cinco anos.")));
}
