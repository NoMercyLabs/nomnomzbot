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
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.TestRun;
using NomNomzBot.Infrastructure.Tests.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// A script runs one pipeline action through the host (<c>actions.invoke:&lt;type&gt;</c>), through the SAME
/// owner-gated runner the widget path uses. Proves the action really runs with the script's inputs, the JSON
/// result has the widget's {success, output, error, variables} shape, every refusal leaves the action un-run and
/// sets the typed last error, and a test run captures the call without running anything.
/// </summary>
public sealed class ScriptHostBridgeActionInvokeTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-0000000000a1");
    private static readonly Guid Owner = Guid.Parse("0192c000-0000-7000-8000-0000000000a3");
    private const string Key = "actions.invoke:tts_synthesize";

    private readonly WidgetSqliteTestDatabase _database = WidgetSqliteTestDatabase.Open();
    private readonly WidgetTestDbContext _db;
    private readonly IActionAuthorizationService _authorization =
        Substitute.For<IActionAuthorizationService>();
    private readonly IRateLimiterPartitionStore _rateLimiter =
        Substitute.For<IRateLimiterPartitionStore>();
    private readonly RecordingAction _action = new();

    public ScriptHostBridgeActionInvokeTests()
    {
        _db = _database.NewContext();
        _db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Owner,
                TwitchChannelId = "123456789012",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = Channel.ToString("N"),
            }
        );
        _db.SaveChanges();
        _authorization
            .AuthorizeActionAsync(Owner, Channel, "pipelines:write", Arg.Any<CancellationToken>())
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

    public void Dispose()
    {
        _db.Dispose();
        _database.Dispose();
    }

    private sealed class RecordingAction : ICommandAction
    {
        public int Runs { get; private set; }
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
            Runs++;
            Context = ctx;
            Definition = action;
            ctx.Variables["tts.durationMs"] = "4200";
            return Task.FromResult(Behavior());
        }
    }

    private ScriptHostBridge Build() =>
        new(
            Channel,
            Guid.CreateVersion7().ToString(),
            null,
            Substitute.For<IChatProvider>(),
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            Substitute.For<IScriptStorageService>(),
            Substitute.For<ITtsDispatchService>(),
            Substitute.For<IWidgetService>(),
            Substitute.For<IWidgetEventNotifier>(),
            Substitute.For<IRewardService>(),
            Substitute.For<IViewerAnalyticsService>(),
            Substitute.For<ITtsConfigService>(),
            Substitute.For<IScheduledPipelineService>(),
            _db,
            Substitute.For<ISevenTvUserPaintResolver>(),
            new OwnerActionService(
                _db,
                [_action],
                _authorization,
                _rateLimiter,
                NullLogger<OwnerActionService>.Instance
            )
        );

    private static string? Call(IScriptHostBridge bridge, string key, params string[] args) =>
        bridge.Resolve(key)(key, args, CancellationToken.None);

    private static string LastErrorCode(IScriptHostBridge bridge)
    {
        string? json = Call(bridge, "last.error");
        json.Should().NotBeNull("the call failed, so there is an error to read");
        return JObject.Parse(json!)["code"]!.Value<string>()!;
    }

    private static void ShouldHaveWidgetResultShape(JObject result) =>
        result
            .Properties()
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo("success", "output", "error", "variables");

    [Fact]
    public void A_granted_action_runs_as_the_owner_and_returns_the_widget_result_shape()
    {
        ScriptHostBridge bridge = Build();

        string? json = Call(
            bridge,
            Key,
            """{"text":"Hello chat","rate":-5}""",
            """{"redemption.id":"r-1"}"""
        );

        JObject result = JObject.Parse(json!);
        ShouldHaveWidgetResultShape(result);
        result["success"]!.Value<bool>().Should().BeTrue();
        result["output"]!.Value<string>().Should().Be("spoken");
        result["error"]!.Type.Should().Be(JTokenType.Null);
        result["variables"]!["tts.durationMs"]!.Value<string>().Should().Be("4200");
        result["variables"]!["redemption.id"]!.Value<string>().Should().Be("r-1");

        _action.Runs.Should().Be(1);
        _action.Context!.BroadcasterId.Should().Be(Channel);
        _action.Context.TriggeredByUserId.Should().Be(Owner.ToString());
        _action.Definition!.GetString("text").Should().Be("Hello chat");
        _action.Definition.GetDouble("rate").Should().Be(-5);
        Call(bridge, "last.error").Should().BeNull();
    }

    [Fact]
    public void An_owner_without_the_permission_is_refused_and_the_action_does_not_run()
    {
        _authorization
            .AuthorizeActionAsync(Owner, Channel, "pipelines:write", Arg.Any<CancellationToken>())
            .Returns(Result.Success(false));
        ScriptHostBridge bridge = Build();

        JObject result = JObject.Parse(Call(bridge, Key, """{"text":"x"}""")!);

        ShouldHaveWidgetResultShape(result);
        result["success"]!.Value<bool>().Should().BeFalse();
        result["error"]!.Value<string>().Should().Contain("pipelines:write");
        _action.Runs.Should().Be(0);
        LastErrorCode(bridge).Should().Be("refused");
    }

    [Fact]
    public void An_unknown_action_type_is_not_found_and_nothing_runs()
    {
        ScriptHostBridge bridge = Build();

        JObject result = JObject.Parse(Call(bridge, "actions.invoke:no_such_action")!);

        result["success"]!.Value<bool>().Should().BeFalse();
        _action.Runs.Should().Be(0);
        LastErrorCode(bridge).Should().Be("not_found");
    }

    [Fact]
    public void Parameters_that_are_not_a_json_object_are_an_invalid_argument()
    {
        ScriptHostBridge bridge = Build();

        JObject result = JObject.Parse(Call(bridge, Key, "[1,2]")!);

        result["success"]!.Value<bool>().Should().BeFalse();
        _action.Runs.Should().Be(0);
        LastErrorCode(bridge).Should().Be("invalid_argument");
    }

    [Fact]
    public void Too_many_calls_are_rate_limited()
    {
        _rateLimiter
            .AcquireAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RateLimitLease(false, 0, TimeSpan.FromSeconds(30)));
        ScriptHostBridge bridge = Build();

        JObject result = JObject.Parse(Call(bridge, Key)!);

        result["success"]!.Value<bool>().Should().BeFalse();
        _action.Runs.Should().Be(0);
        LastErrorCode(bridge).Should().Be("rate_limited");
    }

    [Fact]
    public void An_action_that_fails_returns_its_error_and_records_upstream_failed()
    {
        _action.Behavior = () => ActionResult.Failure("The voice is not available.");
        ScriptHostBridge bridge = Build();

        JObject result = JObject.Parse(Call(bridge, Key, """{"text":"x"}""")!);

        result["success"]!.Value<bool>().Should().BeFalse();
        result["error"]!.Value<string>().Should().Be("The voice is not available.");
        _action.Runs.Should().Be(1);
        LastErrorCode(bridge).Should().Be("upstream_failed");
    }

    [Fact]
    public void A_test_run_records_the_call_and_runs_nothing()
    {
        CaptureSink sink = new();
        CaptureScriptHostBridge capture = new(Build(), sink, (_, _) => null);

        JObject result = JObject.Parse(Call(capture, Key, """{"text":"Hello chat"}""")!);

        ShouldHaveWidgetResultShape(result);
        result["success"]!.Value<bool>().Should().BeTrue();
        _action.Runs.Should().Be(0);
        sink.Effects.Should().ContainSingle();
        sink.Effects[0].Name.Should().Be(Key);
        sink.Effects[0].ArgsPreview.Should().Contain("Hello chat");
        Call(capture, "last.error").Should().BeNull();
    }
}
