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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Events;
using NomNomzBot.Infrastructure.Widgets.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// The <c>show_raid_recap</c> pipeline action (raid-recap.md §7): it builds the recap of the channel's latest
/// stream and sends exactly one <c>raid_recap.show</c> event, carrying that recap, to the channel's
/// <c>raid_recap</c> system widget. Fail-closed: no open widget or no stream is a typed failure and nothing is
/// sent.
/// </summary>
public sealed class ShowRaidRecapActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid WidgetId = Guid.Parse("0192a000-0000-7000-8000-0000000000ee");

    private readonly IWidgetService _widgets = Substitute.For<IWidgetService>();
    private readonly IWidgetEventNotifier _overlay = Substitute.For<IWidgetEventNotifier>();
    private readonly IStreamStatsService _stats = Substitute.For<IStreamStatsService>();

    private ShowRaidRecapAction Action() =>
        new(
            new Infrastructure.Dashboard.Stats.StreamRecapService(
                Identity.AuthTestBuilder.NewContext(),
                _stats,
                TimeProvider.System
            ),
            _widgets,
            _overlay,
            _events
        );

    private readonly IEventBus _events = Substitute.For<IEventBus>();

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
        };

    private static ActionDefinition Def() => new() { Type = "show_raid_recap" };

    private static StreamStatsDto Recap() =>
        new()
        {
            Stream = new() { Id = "s-42" },
            Duration = 5400,
            Followers = 7,
        };

    private void SurfaceResolves(
        bool enabled = true,
        bool attached = true,
        Dictionary<string, object?>? settings = null
    ) =>
        _widgets
            .EnsureSystemWidgetAsync(
                Broadcaster.ToString(),
                "raid_recap",
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new WidgetDetail(
                        WidgetId,
                        "Raid recap",
                        null,
                        "vue",
                        "<template/>",
                        enabled,
                        null,
                        null,
                        null,
                        settings ?? new(),
                        [],
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow,
                        false,
                        attached,
                        false
                    )
                )
            );

    private void RecapResolves(StreamStatsDto recap) =>
        _stats
            .GetAsync(
                Broadcaster,
                null,
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(recap));

    [Fact]
    public async Task Requests_the_metrics_named_in_the_widget_settings()
    {
        SurfaceResolves(
            settings: new()
            {
                ["metrics"] = new List<object?> { "followers", " bits " },
            }
        );
        RecapResolves(Recap());

        await Action().ExecuteAsync(Ctx(), Def());

        await _stats
            .Received(1)
            .GetAsync(
                Broadcaster,
                null,
                Arg.Is<IReadOnlyCollection<string>?>(m =>
                    m != null && m.SequenceEqual(new[] { "followers", "bits" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Requests_all_metrics_when_the_widget_has_no_metrics_setting()
    {
        SurfaceResolves();
        RecapResolves(Recap());

        await Action().ExecuteAsync(Ctx(), Def());

        await _stats.Received(1).GetAsync(Broadcaster, null, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sends_exactly_one_raid_recap_show_event_carrying_the_latest_stream_stats()
    {
        SurfaceResolves();
        StreamStatsDto recap = Recap();
        RecapResolves(recap);

        ActionResult result = await Action().ExecuteAsync(Ctx(), Def());

        result.Succeeded.Should().BeTrue();
        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                WidgetId,
                "raid_recap.show",
                Arg.Is<object?>(d => ReferenceEquals(d, recap)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_widget_no_browser_source_has_open_fails_with_a_typed_failure_and_sends_nothing()
    {
        SurfaceResolves(attached: false);
        RecapResolves(Recap());

        ActionResult result = await Action().ExecuteAsync(Ctx(), Def());

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("raid_recap").And.Contain("open");
        await _overlay
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default, default, default!, default);
    }

    [Fact]
    public async Task A_recap_nobody_could_see_tells_the_owner_through_the_action_required_inbox()
    {
        SurfaceResolves(attached: false);
        RecapResolves(Recap());

        await Action().ExecuteAsync(Ctx(), Def());

        await _events
            .Received(1)
            .PublishAsync(
                Arg.Is<OwnerActionFailedEvent>(e =>
                    e.BroadcasterId == Broadcaster
                    && e.ActionType == "show_raid_recap"
                    && e.Source == "pipeline"
                    && e.Error.Contains("Raid Recap browser source")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_recap_that_reaches_an_open_widget_raises_no_owner_error()
    {
        SurfaceResolves();
        RecapResolves(Recap());

        await Action().ExecuteAsync(Ctx(), Def());

        await _events
            .DidNotReceiveWithAnyArgs()
            .PublishAsync<OwnerActionFailedEvent>(default!, default);
    }

    [Fact]
    public async Task A_disabled_widget_fails_and_sends_nothing()
    {
        SurfaceResolves(enabled: false);
        RecapResolves(Recap());

        ActionResult result = await Action().ExecuteAsync(Ctx(), Def());

        result.Succeeded.Should().BeFalse();
        await _overlay
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default, default, default!, default);
    }

    [Fact]
    public async Task A_channel_with_no_stream_fails_and_sends_nothing()
    {
        SurfaceResolves();
        _stats
            .GetAsync(
                Broadcaster,
                null,
                Arg.Any<IReadOnlyCollection<string>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Errors.NotFound<StreamStatsDto>("Stream", "latest"));

        ActionResult result = await Action().ExecuteAsync(Ctx(), Def());

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("stream");
        await _overlay
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default, default, default!, default);
    }

    [Fact]
    public async Task A_missing_widget_surface_fails_and_sends_nothing()
    {
        _widgets
            .EnsureSystemWidgetAsync(Arg.Any<string>(), "raid_recap", Arg.Any<CancellationToken>())
            .Returns(Errors.NotFound<WidgetDetail>("Widget", "raid_recap"));
        RecapResolves(Recap());

        ActionResult result = await Action().ExecuteAsync(Ctx(), Def());

        result.Succeeded.Should().BeFalse();
        await _overlay
            .DidNotReceiveWithAnyArgs()
            .SendWidgetEventAsync(default, default, default!, default);
    }
}
