// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the <c>{custom.&lt;name&gt;.&lt;field&gt;}</c> template variable end to end through the real
/// <see cref="TemplateResolver"/> over the real <see cref="MemoryCacheService"/> D4 latest-value cache: it
/// substitutes the field's latest ingested value, and a missing source or missing field resolves to an empty
/// string without throwing. The cache is seeded with the exact key
/// (<c>customdata:{broadcasterId}:{name}</c>) and shape (<see cref="CustomDataLatestValue"/>) that
/// <see cref="CustomDataIngestService"/> writes.
/// </summary>
public sealed class CustomDataTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000e001");

    private readonly ICacheService _cache;
    private readonly TemplateResolver _resolver;

    public CustomDataTemplateResolverTests()
    {
        // Real in-memory cache, registered as the singleton ICacheService the resolver reads through a scope.
        ServiceCollection services = new();
        services.AddMemoryCache();
        services.AddLogging();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        ServiceProvider provider = services.BuildServiceProvider();

        _cache = provider.GetRequiredService<ICacheService>();

        _resolver = new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private async Task SeedHeartrateAsync()
    {
        // Mirrors CustomDataIngestService's write: same key format and same record shape.
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bpm"] = "128",
        };
        await _cache.SetAsync(
            $"customdata:{Channel}:heartrate",
            new CustomDataLatestValue("heartrate", fields, "{\"hr\":128}", DateTime.UtcNow),
            TimeSpan.FromHours(24)
        );
    }

    [Fact]
    public async Task CustomData_KnownField_SubstitutesLatestValue()
    {
        await SeedHeartrateAsync();

        string resolved = await _resolver.ResolveAsync(
            "BPM is {custom.heartrate.bpm}",
            new Dictionary<string, string>(),
            Channel
        );

        resolved.Should().Be("BPM is 128");
    }

    [Fact]
    public async Task CustomData_UnknownSource_ResolvesToEmptyString_WithoutThrowing()
    {
        await SeedHeartrateAsync();

        string resolved = await _resolver.ResolveAsync(
            "x{custom.nope.bpm}y",
            new Dictionary<string, string>(),
            Channel
        );

        // A source that was never ingested expands to empty — surrounding text survives, nothing throws.
        resolved.Should().Be("xy");
    }

    [Fact]
    public async Task CustomData_KnownSourceUnknownField_ResolvesToEmptyString()
    {
        await SeedHeartrateAsync();

        string resolved = await _resolver.ResolveAsync(
            "v[{custom.heartrate.spo2}]",
            new Dictionary<string, string>(),
            Channel
        );

        // The source exists but the field was never mapped — the field expands to empty, not the raw token.
        resolved.Should().Be("v[]");
    }
}
