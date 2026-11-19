using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Rolehound.Api.Infrastructure.Persistence;

namespace Rolehound.Api.Infrastructure.Security;

/// <summary>
/// ASP.NET Data Protection protects session cookies and, from the onboarding
/// slice on, OpenRouter keys. Its key ring lives in the database so every
/// instance and restart shares it; outside development each key is encrypted
/// with a certificate that comes from the environment, so the database alone
/// never reveals it (NFR-SEC-03).
/// </summary>
internal static class DataProtectionExtensions
{
    public const string CertificateSetting = "DataProtection:CertificateBase64";
    public const string CertificatePasswordSetting = "DataProtection:CertificatePassword";

    public static IServiceCollection AddKeyRing(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var builder = services.AddDataProtection()
            .SetApplicationName("rolehound")
            .PersistKeysToDbContext<RolehoundDbContext>();

        var certificateBase64 = configuration[CertificateSetting];
        if (!string.IsNullOrWhiteSpace(certificateBase64))
        {
            var certificate = X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(certificateBase64),
                configuration[CertificatePasswordSetting]);
            builder.ProtectKeysWithCertificate(certificate);
        }
        else if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException($"{CertificateSetting} is required outside Development: the key ring must not be readable from the database alone.");
        }

        return services;
    }
}
