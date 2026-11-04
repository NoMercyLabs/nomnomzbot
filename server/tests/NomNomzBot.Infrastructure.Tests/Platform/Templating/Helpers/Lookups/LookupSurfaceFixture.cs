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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Lookups;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Lookups;

/// <summary>Valid lookup arguments and fixed outside-source answers for the surface matrix.</summary>
internal static class LookupSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) =>
        key switch
        {
            "lookup.currency" => [":2|EUR|USD"],
            "lookup.currency.rate" => [":EUR|USD"],
            "lookup.game" => [":Minecraft.boxart"],
            "lookup.translate" => [":nl|hello world"],
            "lookup.translate.chat" => [":nl"],
            "lookup.twitch" => [":samplestreamer.url"],
            _ => [],
        };

    public static void Configure(ServiceCollection services)
    {
        StubLookupHandler handler = new();
        services.AddSingleton(handler);
        LookupHttp http = new(() => new HttpClient(handler, disposeHandler: false));
        LookupCache cache = new(new MemoryCache(new MemoryCacheOptions()));
        OpenMeteoLocationClient location = new(http, cache);
        LookupSources sources = new(
            location,
            new OpenMeteoWeatherClient(location, http, cache),
            new FrankfurterCurrencyClient(http, cache),
            new MyMemoryTranslationClient(http, cache),
            cache,
            true
        );
        foreach (
            ServiceDescriptor descriptor in services
                .Where(d => d.ImplementationType == typeof(LookupTemplateHelpers))
                .ToList()
        )
            services.Remove(descriptor);
        services.AddSingleton<ITemplateHelperProvider>(sp => new LookupTemplateHelpers(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sources
        ));
    }
}
