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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves S070: <c>Users.Timezone</c> (collected in the setup wizard / Settings → Bot basics) is a real,
/// live-read consumer — <c>{time}</c>/<c>{date}</c> render in the channel owner's configured timezone
/// instead of always UTC. Before this slice, <see cref="TemplateResolver"/> formatted the dispatcher's
/// UTC clock unconditionally; the timezone was persisted (<c>ChannelService.UpdateBasicsAsync</c>) but
/// never read back by anything that renders a timestamp.
/// </summary>
public sealed class TemplateResolverTimezoneTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000d001");

    private static TemplateResolver BuildResolver(string? timezone)
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "999",
                    ChannelName = "stoney_eagle",
                    Timezone = timezone,
                }
            );

        // A fixed UTC instant: 2026-08-31T23:30:00Z is 2026-09-01T01:30:00 in Europe/Amsterdam (UTC+2, DST) —
        // deliberately crossing midnight so a wrong/UTC render would also disagree on {date}, not just {time}.
        DateTimeOffset fixedUtc = new(2026, 8, 31, 23, 30, 0, TimeSpan.Zero);
        return new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            registry,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(fixedUtc)
        );
    }

    [Fact]
    public async Task Time_and_date_render_in_the_channels_configured_timezone_not_UTC()
    {
        TemplateResolver resolver = BuildResolver("Europe/Amsterdam");

        string resolved = await resolver.ResolveAsync(
            "{time} on {date}",
            new Dictionary<string, string>(),
            Channel
        );

        resolved.Should().Be("01:30:00 on 2026-09-01");
    }

    [Fact]
    public async Task Time_falls_back_to_UTC_when_the_channel_has_no_timezone_configured()
    {
        TemplateResolver resolver = BuildResolver(null);

        string resolved = await resolver.ResolveAsync(
            "{time} on {date}",
            new Dictionary<string, string>(),
            Channel
        );

        resolved.Should().Be("23:30:00 on 2026-08-31");
    }

    [Fact]
    public async Task TimeUtc_always_stays_UTC_even_when_a_channel_timezone_is_configured()
    {
        TemplateResolver resolver = BuildResolver("Europe/Amsterdam");

        string resolved = await resolver.ResolveAsync(
            "{time.utc}",
            new Dictionary<string, string>(),
            Channel
        );

        resolved.Should().Be("23:30:00 UTC");
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
