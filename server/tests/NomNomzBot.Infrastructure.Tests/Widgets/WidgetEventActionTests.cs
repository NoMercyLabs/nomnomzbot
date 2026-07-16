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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Widgets.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Behavior of the <c>widget_event</c> pipeline action (widgets-overlays.md §6): it pushes exactly one event
/// to the overlay group when the target widget exists AND is enabled in the executing tenant, materializes the
/// JSON <c>data</c> payload into a plain CLR graph, and fails closed (no push) otherwise.
/// </summary>
public sealed class WidgetEventActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid WidgetId = Guid.Parse("0192a000-0000-7000-8000-0000000000ff");

    private readonly IWidgetService _widgets = Substitute.For<IWidgetService>();
    private readonly IWidgetEventNotifier _overlay = Substitute.For<IWidgetEventNotifier>();

    private WidgetEventAction Action() => new(_widgets, _overlay);

    private static PipelineExecutionContext Ctx() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = "user",
            TriggeredByDisplayName = "User",
            MessageId = "msg",
            RawMessage = "",
        };

    // JSON literals use the placeholder WID for the widget id (avoids raw-string brace-interpolation clashes).
    private static ActionDefinition Def(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<ActionDefinition>(
            json.Replace("WID", WidgetId.ToString())
        )!;

    private void WidgetResolves(bool enabled) =>
        _widgets
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetDetail(
                        WidgetId,
                        "Ticker",
                        null,
                        "vue",
                        "<template/>",
                        enabled,
                        null,
                        null,
                        null,
                        new Dictionary<string, object?>(),
                        [],
                        null,
                        null,
                        DateTime.UtcNow,
                        DateTime.UtcNow
                    )
                )
            );

    [Fact]
    public async Task Enabled_widget_pushes_one_event_with_the_materialized_data_payload()
    {
        WidgetResolves(enabled: true);
        object? capturedData = null;
        await _overlay.SendWidgetEventAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Do<object?>(d => capturedData = d),
            Arg.Any<CancellationToken>()
        );

        ActionResult result = await Action()
            .ExecuteAsync(
                Ctx(),
                Def(
                    """
                    {"type":"widget_event","widget_id":"WID","event_type":"alert","data":{"user":"alice","count":5,"vip":true}}
                    """
                )
            );

        result.Succeeded.Should().BeTrue();

        // The event landed on the resolved tenant + widget with the right type — one push, not many.
        await _overlay
            .Received(1)
            .SendWidgetEventAsync(
                Broadcaster,
                WidgetId,
                "alert",
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );

        // The JSON data was materialized to a plain CLR graph (not a raw JsonElement) with correct types.
        capturedData.Should().BeOfType<Dictionary<string, object?>>();
        Dictionary<string, object?> data = (Dictionary<string, object?>)capturedData!;
        data["user"].Should().Be("alice");
        data["count"].Should().Be(5L);
        data["vip"].Should().Be(true);
    }

    [Fact]
    public async Task Disabled_widget_fails_closed_and_never_pushes()
    {
        WidgetResolves(enabled: false);

        ActionResult result = await Action()
            .ExecuteAsync(
                Ctx(),
                Def("""{"type":"widget_event","widget_id":"WID","event_type":"alert"}""")
            );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("disabled");
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Unknown_widget_fails_closed_and_never_pushes()
    {
        _widgets
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<WidgetDetail>("not found", "NOT_FOUND"));

        ActionResult result = await Action()
            .ExecuteAsync(
                Ctx(),
                Def("""{"type":"widget_event","widget_id":"WID","event_type":"alert"}""")
            );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
        await _overlay
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Missing_widget_id_fails_without_touching_the_service()
    {
        ActionResult result = await Action()
            .ExecuteAsync(Ctx(), Def("""{"type":"widget_event","event_type":"alert"}"""));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("widget_id");
        await _widgets
            .DidNotReceive()
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_event_type_fails()
    {
        ActionResult result = await Action()
            .ExecuteAsync(Ctx(), Def("""{"type":"widget_event","widget_id":"WID"}"""));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("event_type");
    }

    [Fact]
    public void ActionType_is_widget_event()
    {
        Action().ActionType.Should().Be("widget_event");
    }
}
