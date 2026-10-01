// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// widget-sdk.md §8: a widget runs pipeline actions as its channel owner, gated only by the owner's IAM. Proves the
/// action really runs with the widget's parameters and variables, its outputs come back, and every refusal (wrong
/// channel, turned off, unknown action, no permission, too many calls) leaves the action un-run.
/// </summary>
public sealed class WidgetActionServiceTests : IDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000000a1");
    private static readonly Guid OtherBroadcaster = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000a2"
    );
    private static readonly Guid Owner = Guid.Parse("0192b000-0000-7000-8000-0000000000a3");

    private readonly WidgetSqliteTestDatabase _database = WidgetSqliteTestDatabase.Open();
    private readonly IActionAuthorizationService _authorization =
        Substitute.For<IActionAuthorizationService>();
    private readonly IRateLimiterPartitionStore _rateLimiter =
        Substitute.For<IRateLimiterPartitionStore>();
    private readonly RecordingAction _action = new();

    public WidgetActionServiceTests()
    {
        _authorization
            .AuthorizeActionAsync(
                Owner,
                Broadcaster,
                "pipelines:write",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(true));
        _rateLimiter
            .AcquireAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RateLimitLease(true, 59, TimeSpan.Zero));
    }

    public void Dispose() => _database.Dispose();

    /// <summary>Stands in for a real action: records what it was handed and writes an output variable.</summary>
    private sealed class RecordingAction : ICommandAction
    {
        public PipelineExecutionContext? Context { get; private set; }
        public ActionDefinition? Definition { get; private set; }
        public Func<ActionResult> Behavior { get; set; } = () => ActionResult.Success("spoken");

        public string ActionType => "tts_synthesize";
        public LocalizedText Category => new("pipeline.category.tts");
        public LocalizedText Description => new("pipeline.tts_synthesize.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            Context = ctx;
            Definition = action;
            ctx.Variables["tts.durationMs"] = "4200";
            return Task.FromResult(Behavior());
        }
    }

    private async Task<Guid> SeedAsync(Guid broadcasterId, bool enabled = true)
    {
        await using WidgetTestDbContext db = _database.NewContext();
        if (!db.Channels.Any(c => c.Id == broadcasterId))
            db.Channels.Add(
                new()
                {
                    Id = broadcasterId,
                    OwnerUserId = broadcasterId == Broadcaster ? Owner : Guid.CreateVersion7(),
                    TwitchChannelId = broadcasterId.ToString("N")[..12],
                    Name = "teststreamer",
                    NameNormalized = "teststreamer",
                    OverlayToken = broadcasterId.ToString("N"),
                }
            );
        Widget widget = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            Name = "BSOD",
            IsEnabled = enabled,
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        return widget.Id;
    }

    private WidgetActionService Service(WidgetTestDbContext db) =>
        new(db, [_action], _authorization, _rateLimiter, NullLogger<WidgetActionService>.Instance);

    private static WidgetActionRequest Request(
        Guid widgetId,
        string actionType = "tts_synthesize"
    ) =>
        new(
            Broadcaster,
            widgetId,
            actionType,
            new Dictionary<string, JsonElement>
            {
                ["text"] = JsonSerializer.SerializeToElement("Your PC ran into a problem."),
                ["rate"] = JsonSerializer.SerializeToElement(-5),
            },
            new Dictionary<string, string> { ["redemption.id"] = "redemption-1" }
        );

    [Fact]
    public async Task Runs_the_action_as_the_owner_with_the_widget_inputs_and_returns_its_outputs()
    {
        Guid widgetId = await SeedAsync(Broadcaster);
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db).InvokeAsync(Request(widgetId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().BeTrue();
        result.Value.Output.Should().Be("spoken");
        result.Value.Variables["tts.durationMs"].Should().Be("4200");
        result.Value.Variables["redemption.id"].Should().Be("redemption-1");

        _action.Context!.BroadcasterId.Should().Be(Broadcaster);
        _action.Context.TriggeredByUserId.Should().Be(Owner.ToString());
        _action.Context.TriggeredByDisplayName.Should().Be("BSOD");
        _action.Definition!.Type.Should().Be("tts_synthesize");
        _action.Definition.GetString("text").Should().Be("Your PC ran into a problem.");
        _action.Definition.GetDouble("rate").Should().Be(-5);
        await _rateLimiter
            .Received(1)
            .AcquireAsync(
                $"widget-action:{widgetId}",
                60,
                TimeSpan.FromMinutes(1),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_action_type_is_matched_regardless_of_case()
    {
        Guid widgetId = await SeedAsync(Broadcaster);
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db)
            .InvokeAsync(Request(widgetId, "TTS_Synthesize"));

        result.Value.Succeeded.Should().BeTrue();
        _action.Definition!.Type.Should().Be("tts_synthesize");
    }

    [Fact]
    public async Task An_owner_without_the_permission_is_refused_and_nothing_runs()
    {
        Guid widgetId = await SeedAsync(Broadcaster);
        _authorization
            .AuthorizeActionAsync(
                Owner,
                Broadcaster,
                "pipelines:write",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db).InvokeAsync(Request(widgetId));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        _action.Context.Should().BeNull();
    }

    [Fact]
    public async Task A_widget_of_another_channel_is_not_found_and_nothing_runs()
    {
        Guid foreignWidgetId = await SeedAsync(OtherBroadcaster);
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db)
            .InvokeAsync(Request(foreignWidgetId));

        result.ErrorCode.Should().Be("NOT_FOUND");
        _action.Context.Should().BeNull();
    }

    [Fact]
    public async Task A_turned_off_widget_is_refused_and_nothing_runs()
    {
        Guid widgetId = await SeedAsync(Broadcaster, enabled: false);
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db).InvokeAsync(Request(widgetId));

        result.ErrorCode.Should().Be("FORBIDDEN");
        _action.Context.Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_action_type_is_not_found()
    {
        Guid widgetId = await SeedAsync(Broadcaster);
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db)
            .InvokeAsync(Request(widgetId, "format_hard_drive"));

        result.ErrorCode.Should().Be("NOT_FOUND");
        _action.Context.Should().BeNull();
    }

    [Fact]
    public async Task A_widget_over_its_rate_limit_is_refused_and_nothing_runs()
    {
        Guid widgetId = await SeedAsync(Broadcaster);
        _rateLimiter
            .AcquireAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RateLimitLease(false, 0, TimeSpan.FromSeconds(20)));
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db).InvokeAsync(Request(widgetId));

        result.ErrorCode.Should().Be("RATE_LIMITED");
        _action.Context.Should().BeNull();
    }

    [Fact]
    public async Task An_action_that_throws_comes_back_as_a_failed_run_with_the_reason()
    {
        Guid widgetId = await SeedAsync(Broadcaster);
        _action.Behavior = () => throw new InvalidOperationException("provider is down");
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db).InvokeAsync(Request(widgetId));

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().BeFalse();
        result.Value.Error.Should().Be("provider is down");
    }

    [Fact]
    public async Task An_action_that_only_works_inside_a_pipeline_reports_that_instead_of_succeeding()
    {
        Guid widgetId = await SeedAsync(Broadcaster);
        _action.Behavior = () => ActionResult.Suspend();
        await using WidgetTestDbContext db = _database.NewContext();

        Result<WidgetActionOutcome> result = await Service(db).InvokeAsync(Request(widgetId));

        result.Value.Succeeded.Should().BeFalse();
        result.Value.Error.Should().Contain("only run inside a pipeline");
    }
}
