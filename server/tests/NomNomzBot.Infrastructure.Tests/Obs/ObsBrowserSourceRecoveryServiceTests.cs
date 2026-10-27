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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Obs.Recovery;
using NomNomzBot.Infrastructure.Tests.Notifications;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Obs;

/// <summary>
/// Proves the recovery pass against a fake OBS: a loaded browser source whose widget has no live overlay
/// connection gets exactly one refresh press, and nothing else is touched. The fake OBS records every refresh,
/// so each case asserts the real consequence (which sources OBS was told to reload), not a returned flag.
/// </summary>
public sealed class ObsBrowserSourceRecoveryServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f901");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f902");

    private static string Address(Guid widgetId) =>
        $"https://bot.example.test/overlay?widgetId={widgetId}&token=abc123";

    private sealed class Rig : IAsyncDisposable
    {
        public required ActionRequiredInboxServiceTestDbContext Db { get; init; }
        public required IObsControlService Obs { get; init; }
        public required IOverlayPresenceRegistry Presence { get; init; }
        public required ObsBrowserSourceRecoveryService Service { get; init; }
        public required List<string> Refreshed { get; init; }

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        public Widget AddWidget(Guid broadcasterId, bool enabled = true, bool attached = false)
        {
            Widget widget = new()
            {
                BroadcasterId = broadcasterId,
                Name = "Widget",
                IsEnabled = enabled,
            };
            Db.Widgets.Add(widget);
            Db.SaveChanges();
            Presence.IsWidgetAttached(broadcasterId, widget.Id).Returns(attached);
            return widget;
        }

        public void ObsHas(params (string Name, string Url, bool Loaded)[] sources) =>
            ObsHas(sources.Select(s => (s.Name, s.Url, s.Loaded, true)).ToArray());

        public void ObsHas(
            params (string Name, string Url, bool Loaded, bool EnabledInScene)[] sources
        )
        {
            IReadOnlyList<ObsBrowserSourceDto> listed =
            [
                .. sources.Select(s => new ObsBrowserSourceDto(
                    s.Name,
                    s.Url,
                    s.Loaded,
                    s.EnabledInScene
                )),
            ];
            Obs.GetBrowserSourcesAsync(Channel, Arg.Any<CancellationToken>())
                .Returns(Result.Success(listed));
        }
    }

    private static async Task<Rig> BuildAsync(bool refreshSucceeds = true)
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.Channels.AddRange(
            new Channel
            {
                Id = Channel,
                Name = "mine",
                NameNormalized = "mine",
            },
            new Channel
            {
                Id = OtherChannel,
                Name = "other",
                NameNormalized = "other",
            }
        );
        await db.SaveChangesAsync();

        List<string> refreshed = [];
        IObsControlService obs = Substitute.For<IObsControlService>();
        obs.RefreshBrowserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                refreshed.Add(call.ArgAt<string>(1));
                return refreshSucceeds
                    ? Result.Success()
                    : Result.Failure("OBS rejected the request.", "OBS_ERROR");
            });

        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        return new()
        {
            Db = db,
            Obs = obs,
            Presence = presence,
            Refreshed = refreshed,
            Service = new(obs, db, presence, NullLogger<ObsBrowserSourceRecoveryService>.Instance),
        };
    }

    [Fact]
    public async Task A_loaded_source_whose_widget_has_no_connection_is_refreshed_exactly_once()
    {
        await using Rig rig = await BuildAsync();
        Widget dead = rig.AddWidget(Channel);
        rig.ObsHas(("Alerts", Address(dead.Id), true));

        Result<IReadOnlyList<string>> result = await rig.Service.RecoverAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal("Alerts");
        rig.Refreshed.Should().Equal("Alerts");
    }

    [Fact]
    public async Task A_loaded_source_whose_widget_is_connected_is_left_alone()
    {
        await using Rig rig = await BuildAsync();
        Widget alive = rig.AddWidget(Channel, attached: true);
        rig.ObsHas(("Alerts", Address(alive.Id), true));

        Result<IReadOnlyList<string>> result = await rig.Service.RecoverAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        rig.Refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_disconnected_source_of_two_is_refreshed()
    {
        await using Rig rig = await BuildAsync();
        Widget alive = rig.AddWidget(Channel, attached: true);
        Widget dead = rig.AddWidget(Channel);
        rig.ObsHas(("Chat", Address(alive.Id), true), ("Goals", Address(dead.Id), true));

        await rig.Service.RecoverAsync(Channel);

        rig.Refreshed.Should().Equal("Goals");
    }

    [Fact]
    public async Task A_source_OBS_has_unloaded_is_not_refreshed()
    {
        await using Rig rig = await BuildAsync();
        Widget dead = rig.AddWidget(Channel);
        rig.ObsHas(("Hidden alerts", Address(dead.Id), false));

        await rig.Service.RecoverAsync(Channel);

        rig.Refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_switched_off_widget_is_not_refreshed()
    {
        await using Rig rig = await BuildAsync();
        Widget off = rig.AddWidget(Channel, enabled: false);
        rig.ObsHas(("Old alerts", Address(off.Id), true));

        await rig.Service.RecoverAsync(Channel);

        rig.Refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_source_hidden_in_every_OBS_scene_is_not_refreshed()
    {
        await using Rig rig = await BuildAsync();
        Widget dead = rig.AddWidget(Channel);
        rig.ObsHas(("Hidden alerts", Address(dead.Id), true, false));

        Result<IReadOnlyList<string>> result = await rig.Service.RecoverAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        rig.Refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_shown_source_of_a_hidden_and_a_shown_pair_is_refreshed()
    {
        await using Rig rig = await BuildAsync();
        Widget shown = rig.AddWidget(Channel);
        Widget hidden = rig.AddWidget(Channel);
        rig.ObsHas(
            ("Shown", Address(shown.Id), true, true),
            ("Hidden", Address(hidden.Id), true, false)
        );

        await rig.Service.RecoverAsync(Channel);

        rig.Refreshed.Should().Equal("Shown");
    }

    [Fact]
    public async Task The_pass_never_enables_or_shows_anything_in_OBS_and_never_changes_a_widget()
    {
        await using Rig rig = await BuildAsync();
        Widget off = rig.AddWidget(Channel, enabled: false);
        Widget dead = rig.AddWidget(Channel);
        Widget hiddenInObs = rig.AddWidget(Channel);
        rig.ObsHas(
            ("Off in the bot", Address(off.Id), true, true),
            ("Dead", Address(dead.Id), true, true),
            ("Hidden in OBS", Address(hiddenInObs.Id), true, false)
        );
        rig.Obs.ClearReceivedCalls();

        await rig.Service.RecoverAsync(Channel);

        // The only OBS calls the pass may make are the read and the refresh press.
        rig.Obs.ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Distinct()
            .Should()
            .BeSubsetOf([
                nameof(IObsControlService.GetBrowserSourcesAsync),
                nameof(IObsControlService.RefreshBrowserAsync),
            ]);
        rig.Refreshed.Should().Equal("Dead");

        rig.Db.ChangeTracker.Clear();
        (await rig.Db.Widgets.FindAsync(off.Id))!.IsEnabled.Should().BeFalse();
        (await rig.Db.Widgets.FindAsync(dead.Id))!.IsEnabled.Should().BeTrue();
        (await rig.Db.Widgets.FindAsync(hiddenInObs.Id))!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task A_source_that_shows_another_channels_widget_is_not_refreshed()
    {
        await using Rig rig = await BuildAsync();
        Widget foreign = rig.AddWidget(OtherChannel);
        rig.ObsHas(("Foreign", Address(foreign.Id), true));

        await rig.Service.RecoverAsync(Channel);

        rig.Refreshed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://example.test/some-page")]
    [InlineData("https://bot.example.test/overlay?token=abc123")]
    [InlineData("https://bot.example.test/overlay?widgetId=not-a-guid&token=abc123")]
    [InlineData("not an address")]
    public async Task A_source_that_is_not_a_widget_page_is_not_refreshed(string address)
    {
        await using Rig rig = await BuildAsync();
        rig.AddWidget(Channel);
        rig.ObsHas(("Website", address, true));

        Result<IReadOnlyList<string>> result = await rig.Service.RecoverAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        rig.Refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task When_OBS_cannot_be_reached_nothing_is_refreshed_and_the_failure_is_returned()
    {
        await using Rig rig = await BuildAsync();
        rig.Obs.GetBrowserSourcesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsBrowserSourceDto>>(
                    "OBS is not connected.",
                    "OBS_NOT_CONNECTED"
                )
            );

        Result<IReadOnlyList<string>> result = await rig.Service.RecoverAsync(Channel);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_NOT_CONNECTED");
        rig.Refreshed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refresh_OBS_rejects_is_not_reported_as_refreshed()
    {
        await using Rig rig = await BuildAsync(refreshSucceeds: false);
        Widget dead = rig.AddWidget(Channel);
        rig.ObsHas(("Alerts", Address(dead.Id), true));

        Result<IReadOnlyList<string>> result = await rig.Service.RecoverAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        rig.Refreshed.Should().Equal("Alerts");
    }
}
