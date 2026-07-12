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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// Proves <c>!uptime</c> now computes the REAL elapsed time from the channel registry's <c>WentLiveAt</c>
/// (the old stub returned "Check the dashboard for uptime" and never computed anything) and renders it in the
/// channel's personality tone, with the override beating the tone and the offline slot handled separately.
/// </summary>
public sealed class UptimeBuiltinTests
{
    private static readonly Guid Channel = Guid.Parse("0198c111-0000-7000-8000-0000000000a1");
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 20, 0, 0, TimeSpan.Zero);

    private static ITemplateResolver FakeResolver()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return resolver;
    }

    private static UptimeBuiltin Sut(ChannelContext? ctx)
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(ctx);
        return new UptimeBuiltin(
            registry,
            new BuiltinResponseComposer(FakeResolver()),
            new FakeTimeProvider(Now)
        );
    }

    private static ChannelContext LiveContext(TimeSpan since) =>
        new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-1",
            ChannelName = "stoney_eagle",
            IsLive = true,
            WentLiveAt = Now - since,
        };

    private static BuiltinCommandContext Ctx(string personality, string? overrideTemplate = null) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = "u1",
            TriggeringUserDisplayName = "Viewer",
            TriggeringUserLogin = "viewer",
            Personality = personality,
            CustomResponseTemplate = overrideTemplate,
        };

    [Fact]
    public async Task Live_reports_the_real_computed_uptime_not_the_old_stub()
    {
        Result<string> result = await Sut(LiveContext(TimeSpan.FromMinutes(125)))
            .ExecuteAsync(Ctx(PersonalityTone.Informative));

        result.IsSuccess.Should().BeTrue();
        // 125 minutes = 2h 5m — the ACTUAL elapsed time, proving real computation.
        result.Value.Should().Contain("2h 5m");
        result.Value.Should().NotContain("dashboard");

        HashSet<string> expected = ToneTemplateCatalog
            .Get(
                PersonalityTone.Informative,
                BuiltinResponseSlots.Uptime.Key,
                BuiltinResponseSlots.Uptime.Live
            )
            .Select(t => t.Replace("{uptime}", "2h 5m"))
            .ToHashSet();
        expected.Should().Contain(result.Value);
    }

    [Fact]
    public async Task Live_under_an_hour_formats_minutes_and_seconds()
    {
        Result<string> result = await Sut(LiveContext(new TimeSpan(0, 3, 30)))
            .ExecuteAsync(Ctx(PersonalityTone.Informative));

        result.Value.Should().Contain("3m 30s");
    }

    [Fact]
    public async Task Override_wins_and_is_rendered_with_the_real_uptime()
    {
        Result<string> result = await Sut(LiveContext(TimeSpan.FromMinutes(125)))
            .ExecuteAsync(Ctx(PersonalityTone.Sassy, overrideTemplate: "up {uptime} baby"));

        result.Value.Should().Be("up 2h 5m baby");
    }

    [Fact]
    public async Task Offline_uses_the_offline_slot_and_never_reports_an_uptime()
    {
        ChannelContext offlineCtx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-1",
            ChannelName = "stoney_eagle",
            IsLive = false,
        };

        Result<string> result = await Sut(offlineCtx)
            .ExecuteAsync(Ctx(PersonalityTone.Informative));

        HashSet<string> offline = ToneTemplateCatalog
            .Get(
                PersonalityTone.Informative,
                BuiltinResponseSlots.Uptime.Key,
                BuiltinResponseSlots.Uptime.Offline
            )
            .ToHashSet();
        offline.Should().Contain(result.Value);
    }

    [Fact]
    public async Task No_registry_context_is_treated_as_offline()
    {
        Result<string> result = await Sut(ctx: null).ExecuteAsync(Ctx(PersonalityTone.Chill));

        HashSet<string> offline = ToneTemplateCatalog
            .Get(
                PersonalityTone.Chill,
                BuiltinResponseSlots.Uptime.Key,
                BuiltinResponseSlots.Uptime.Offline
            )
            .ToHashSet();
        offline.Should().Contain(result.Value);
    }
}
