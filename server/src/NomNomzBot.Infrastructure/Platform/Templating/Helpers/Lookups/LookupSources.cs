// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure.Platform.Deployment;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

/// <summary>
/// The outside sources of the <c>lookup</c> provider. A test builds it by hand with stub clients; the host
/// builds it from its services. <see cref="OpenMeteoAllowed"/> is false in saas mode: Open-Meteo is free for
/// non-commercial use only, so the weather and local-time forms print their fallback there (catalog D3, option
/// a). Saas mode is RESTRICTED: you may not earn money with NomNomzBot, and that right is reserved to NoMercy Labs.
/// </summary>
internal sealed record LookupSources(
    ILocationClient Location,
    IWeatherClient Weather,
    ICurrencyRateClient Currency,
    ITranslationClient Translation,
    LookupCache Cache,
    bool OpenMeteoAllowed
)
{
    public static LookupSources FromServices(IServiceScopeFactory scopes)
    {
        using IServiceScope scope = scopes.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        IHttpClientFactory? factory = services.GetService<IHttpClientFactory>();
        IMemoryCache memory =
            services.GetService<IMemoryCache>() ?? new MemoryCache(new MemoryCacheOptions());
        LookupHttp http = new(() => factory?.CreateClient("lookups") ?? new HttpClient());
        LookupCache cache = new(memory);
        OpenMeteoLocationClient location = new(http, cache);
        return new LookupSources(
            location,
            new OpenMeteoWeatherClient(location, http, cache),
            new FrankfurterCurrencyClient(http, cache),
            new MyMemoryTranslationClient(http, cache),
            cache,
            services.GetService<DeploymentContext>()?.Mode != DeploymentMode.Saas
        );
    }
}
