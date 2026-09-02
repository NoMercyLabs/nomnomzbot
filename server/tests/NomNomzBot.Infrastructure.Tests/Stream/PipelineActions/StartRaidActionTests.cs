// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Proves the start_raid action: a login target resolves to its Twitch id via Helix Get Users before the raid
/// fires on the broadcaster's tenant, a numeric id passes straight through, an unknown target is a typed
/// failure that never reaches the raids API, a failed lookup is reported distinctly from "not found", an
/// offline target is refused before the raid fires, the raid fires BEFORE the optional post-fire delay,
/// an "already raiding" conflict is tolerated as success, a missing scope routes into the re-grant message
/// instead of a generic failure, a successful raid publishes RaidSentEvent, and a leading "@" is stripped
/// whether it arrives as a literal or via a "{args.N}" variable.
/// </summary>
public sealed class StartRaidActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b301");

    private static PipelineExecutionContext Ctx()
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!raid target",
        };
        ctx.Variables["args.1"] = "coolstreamer";
        return ctx;
    }

    private static ActionDefinition Raid(string target, int? delaySeconds = null)
    {
        Dictionary<string, JsonElement> parameters = new()
        {
            ["target"] = JsonSerializer.SerializeToElement(target),
        };
        if (delaySeconds is { } delay)
            parameters["delay_seconds"] = JsonSerializer.SerializeToElement(delay);
        return new() { Type = "start_raid", Parameters = parameters };
    }

    private static TwitchUser User(string id, string login) =>
        new(
            Id: id,
            Login: login,
            DisplayName: login,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static TwitchStream LiveStream(string userId) =>
        new(
            Id: "s1",
            UserId: userId,
            UserLogin: userId,
            UserName: userId,
            GameId: "1",
            GameName: "Game",
            Type: "live",
            Title: "title",
            Tags: [],
            ViewerCount: 1,
            StartedAt: DateTimeOffset.UnixEpoch,
            Language: "en",
            ThumbnailUrl: "",
            IsMature: false
        );

    private static (
        StartRaidAction Sut,
        ITwitchRaidsApi Raids,
        ITwitchUsersApi Users,
        ITwitchStreamsApi Streams,
        IEventBus EventBus
    ) Build()
    {
        ITwitchRaidsApi raids = Substitute.For<ITwitchRaidsApi>();
        raids
            .StartRaidAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TwitchRaid(DateTimeOffset.UnixEpoch, false)));
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        // Default: every target is live, so tests not exercising the live-check don't need to stub it.
        streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
                Result.Success(
                    new TwitchPage<TwitchStream>(
                        [LiveStream(callInfo.Arg<TwitchStreamsFilter>().UserIds![0])],
                        null,
                        1
                    )
                )
            );
        IEventBus eventBus = Substitute.For<IEventBus>();
        return (
            new(raids, users, streams, eventBus, NullLogger<StartRaidAction>.Instance),
            raids,
            users,
            streams,
            eventBus
        );
    }

    [Fact]
    public async Task A_login_target_resolves_to_its_id_and_the_raid_fires_on_the_tenant()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users, _, _) = Build();
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "coolstreamer"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("789", "coolstreamer")]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("@CoolStreamer"));

        result.Succeeded.Should().BeTrue();
        await raids.Received(1).StartRaidAsync(Channel, "789", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_numeric_target_raids_without_a_users_lookup()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users, _, _) = Build();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456"));

        result.Succeeded.Should().BeTrue();
        await raids.Received(1).StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>());
        await users.DidNotReceiveWithAnyArgs().GetUsersByLoginsAsync(default!, default);
    }

    [Fact]
    public async Task An_unknown_target_is_a_typed_failure_that_never_reaches_the_raids_api()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users, _, _) = Build();
        users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("ghost_channel"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ghost_channel").And.Contain("not found");
        await raids.DidNotReceiveWithAnyArgs().StartRaidAsync(default, default!, default);
    }

    [Fact]
    public async Task A_failed_lookup_is_distinguished_from_a_target_that_was_not_found()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users, _, _) = Build();
        users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<TwitchUser>>("Twitch is unreachable", "TWITCH_ERROR")
            );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("coolstreamer"));

        result.Succeeded.Should().BeFalse();
        result
            .ErrorMessage.Should()
            .Contain("could not look up")
            .And.Contain("Twitch is unreachable");
        result.ErrorMessage.Should().NotContain("not found");
        await raids.DidNotReceiveWithAnyArgs().StartRaidAsync(default, default!, default);
    }

    [Fact]
    public async Task A_missing_target_is_a_typed_failure()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _, _, _) = Build();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("  "));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("target");
        await raids.DidNotReceiveWithAnyArgs().StartRaidAsync(default, default!, default);
    }

    [Fact]
    public async Task An_offline_target_is_refused_before_the_raid_fires()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _, ITwitchStreamsApi streams, _) = Build();
        streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new TwitchPage<TwitchStream>([], null, 0)));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not currently live");
        await raids.DidNotReceiveWithAnyArgs().StartRaidAsync(default, default!, default);
    }

    [Fact]
    public async Task A_helix_refusal_surfaces_as_the_actions_failure()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _, _, _) = Build();
        raids
            .StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchRaid>("The target channel errored.", "TWITCH_ERROR"));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("The target channel errored.");
    }

    [Fact]
    public async Task An_already_raiding_conflict_is_tolerated_as_success()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _, _, IEventBus eventBus) = Build();
        raids
            .StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchRaid>("Already raiding.", TwitchErrorCodes.Conflict));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456"));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Contain("already in progress");
        await eventBus
            .Received(1)
            .PublishAsync(Arg.Any<RaidSentEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_scope_routes_into_a_regrant_message_not_a_generic_failure()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _, _, IEventBus eventBus) = Build();
        raids
            .StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchRaid>("Missing required scope.", TwitchErrorCodes.MissingScope)
            );

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("re-grant").And.Contain("re-authorize");
        await eventBus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<RaidSentEvent>(), default);
    }

    [Fact]
    public async Task A_successful_raid_publishes_RaidSentEvent()
    {
        (StartRaidAction sut, _, _, _, IEventBus eventBus) = Build();

        await sut.ExecuteAsync(Ctx(), Raid("123456"));

        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<RaidSentEvent>(e => e.BroadcasterId == Channel && e.ToUserId == "123456"),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// <c>wait_until_raid_fires</c> reads this variable to re-anchor to Twitch's actual auto-fire
    /// deadline instead of trusting a blind sum of fixed waits — proves start_raid actually stamps it,
    /// close to "now + TwitchRaidWindowSeconds", the instant the raid call succeeds.
    /// </summary>
    [Fact]
    public async Task A_successful_raid_stamps_the_auto_fire_deadline()
    {
        (StartRaidAction sut, _, _, _, _) = Build();
        PipelineExecutionContext ctx = Ctx();
        DateTime before = DateTime.UtcNow;

        await sut.ExecuteAsync(ctx, Raid("123456"));

        DateTime after = DateTime.UtcNow;
        ctx.Variables.Should().ContainKey("raid.fires_at_utc_ticks");
        DateTime firesAt = new(
            long.Parse(ctx.Variables["raid.fires_at_utc_ticks"]),
            DateTimeKind.Utc
        );
        firesAt
            .Should()
            .BeOnOrAfter(before.AddSeconds(StartRaidAction.TwitchRaidWindowSeconds))
            .And.BeOnOrBefore(after.AddSeconds(StartRaidAction.TwitchRaidWindowSeconds + 1));
    }

    [Fact]
    public async Task The_raid_fires_before_the_post_fire_delay_elapses()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _, _, _) = Build();

        // Fires the raid the instant we're called, before the caller's own delay has had a chance to elapse —
        // proven by observing the raid call from inside the substitute callback, timed against a stopwatch
        // that keeps running through the action's post-fire delay.
        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeSpan? raidFiredAt = null;
        raids
            .StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                raidFiredAt = stopwatch.Elapsed;
                return Result.Success(new TwitchRaid(DateTimeOffset.UnixEpoch, false));
            });

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456", delaySeconds: 1));
        stopwatch.Stop();

        result.Succeeded.Should().BeTrue();
        raidFiredAt.Should().NotBeNull();
        raidFiredAt!.Value.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(950));
    }

    [Fact]
    public async Task A_template_variable_target_resolves_from_the_pipeline_variables()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users, _, _) = Build();
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "coolstreamer"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("789", "coolstreamer")]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("{args.1}"));

        result.Succeeded.Should().BeTrue();
        await raids.Received(1).StartRaidAsync(Channel, "789", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_leading_at_sign_is_stripped_when_it_arrives_via_a_variable()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users, _, _) = Build();
        PipelineExecutionContext ctx = Ctx();
        ctx.Variables["args.1"] = "@coolstreamer"; // "!raid @someone" seeds args.N with the "@" intact.
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(l => l.Count == 1 && l[0] == "coolstreamer"),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User("789", "coolstreamer")]));

        ActionResult result = await sut.ExecuteAsync(ctx, Raid("{args.1}"));

        result.Succeeded.Should().BeTrue();
        await raids.Received(1).StartRaidAsync(Channel, "789", Arg.Any<CancellationToken>());
    }
}
