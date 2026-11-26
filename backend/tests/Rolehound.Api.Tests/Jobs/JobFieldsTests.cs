using Rolehound.Api.Features.Jobs;
using Rolehound.Connectors;

namespace Rolehound.Api.Tests.Jobs;

public sealed class JobFieldsTests
{
    [Fact]
    public void A_listing_entry_without_title_or_details_keeps_what_is_stored()
    {
        var job = new Job
        {
            Source = JobSource.CareerSite,
            ExternalId = "https://carreiras.exemplo.test/job/1",
            Title = "Analista de Segurança",
            Company = "Exemplo",
            Location = "São Paulo, SP",
            Description = "Descrição completa.",
            Url = "https://carreiras.exemplo.test/job/1",
        };
        var listed = new CollectedJob("https://carreiras.exemplo.test/job/1", string.Empty, string.Empty, string.Empty, string.Empty, "https://carreiras.exemplo.test/job/1", null, null, HasDetails: false);

        var changed = JobFields.Apply(job, listed, null);

        Assert.False(changed);
        Assert.Equal(("Analista de Segurança", "Exemplo", "Descrição completa."), (job.Title, job.Company, job.Description));
    }
}
