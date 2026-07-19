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
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Proves the start_raid action: a login target resolves to its Twitch id via Helix Get Users before the raid
/// fires on the broadcaster's tenant, a numeric id passes straight through, an unknown target is a typed
/// failure that never reaches the raids API, a Helix refusal surfaces as the action's failure, the optional
/// delay is honored as an internal in-action wait (clamped to 90s), and template variables resolve in the
/// target param (the shoutout convention).
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
        if (delaySeconds is int delay)
            parameters["delay_seconds"] = JsonSerializer.SerializeToElement(delay);
        return new ActionDefinition { Type = "start_raid", Parameters = parameters };
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

    private static (StartRaidAction Sut, ITwitchRaidsApi Raids, ITwitchUsersApi Users) Build()
    {
        ITwitchRaidsApi raids = Substitute.For<ITwitchRaidsApi>();
        raids
            .StartRaidAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TwitchRaid(DateTimeOffset.UnixEpoch, false)));
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        return (
            new StartRaidAction(raids, users, NullLogger<StartRaidAction>.Instance),
            raids,
            users
        );
    }

    [Fact]
    public async Task A_login_target_resolves_to_its_id_and_the_raid_fires_on_the_tenant()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users) = Build();
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
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users) = Build();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456"));

        result.Succeeded.Should().BeTrue();
        await raids.Received(1).StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>());
        await users.DidNotReceiveWithAnyArgs().GetUsersByLoginsAsync(default!, default);
    }

    [Fact]
    public async Task An_unknown_target_is_a_typed_failure_that_never_reaches_the_raids_api()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users) = Build();
        users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("ghost_channel"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ghost_channel").And.Contain("not found");
        await raids.DidNotReceiveWithAnyArgs().StartRaidAsync(default, default!, default);
    }

    [Fact]
    public async Task A_missing_target_is_a_typed_failure()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _) = Build();

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("  "));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("target");
        await raids.DidNotReceiveWithAnyArgs().StartRaidAsync(default, default!, default);
    }

    [Fact]
    public async Task A_helix_refusal_surfaces_as_the_actions_failure()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _) = Build();
        raids
            .StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchRaid>("The target channel is offline.", "TWITCH_ERROR"));

        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("The target channel is offline.");
    }

    [Fact]
    public async Task The_delay_is_honored_inside_the_action_before_the_raid_fires()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, _) = Build();

        Stopwatch stopwatch = Stopwatch.StartNew();
        ActionResult result = await sut.ExecuteAsync(Ctx(), Raid("123456", delaySeconds: 1));
        stopwatch.Stop();

        result.Succeeded.Should().BeTrue();
        // The wait is internal to the action (Task.Delay under the pipeline token — documented choice),
        // so the whole execution takes at least the requested second before Helix is called.
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(950));
        await raids.Received(1).StartRaidAsync(Channel, "123456", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_template_variable_target_resolves_from_the_pipeline_variables()
    {
        (StartRaidAction sut, ITwitchRaidsApi raids, ITwitchUsersApi users) = Build();
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
}
