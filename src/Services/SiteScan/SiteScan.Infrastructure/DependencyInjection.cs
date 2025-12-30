using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SiteScan.Application.Abstractions;
using SiteScan.Domain.UrlResolution;
using SiteScan.Infrastructure.Http;

namespace SiteScan.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddUrlResolutionFromOptions(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientNames.UrlResolution)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<UrlResolutionOptions>>().Value;

                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    ConnectTimeout = opts.ConnectTimeout
                };
            });

        services.AddScoped<IUrlResolver, HttpUrlResolver>();
        return services;
    }
}
