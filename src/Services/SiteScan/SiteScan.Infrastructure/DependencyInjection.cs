using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SiteScan.Application.Abstractions;
using SiteScan.Domain.UrlResolution;
using SiteScan.Infrastructure.Http;

namespace SiteScan.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddUrlResolution(this IServiceCollection services, UrlResolutionOptions options)
    {
        services.AddSingleton(options);

        services.AddHttpClient(HttpClientNames.UrlResolution)
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                // Manual redirect handling (required to capture chain and enforce rules)
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                    // Connect timeout configured per acceptance criteria
                    ConnectTimeout = options.ConnectTimeout
                };
            });

        services.AddScoped<IUrlResolver, HttpUrlResolver>();
        return services;
    }
}
