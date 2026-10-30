// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.CustomCode.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// S-RUN-FAILURE-SHAPE: a pipeline run records WHAT KIND of failure ended it, WHICH limit it hit, and the host
/// calls each step made with their durations, so the run detail can say "hit limit X at Y; 12.3 s spent in
/// tts.speak" without reading the error text. These drive the real engine, the real run_code action, the real
/// script runner and the real Jint sandbox; only the outside world (the TTS edge, the plan meter, the grant
/// broker) is stood in for. Each test reads the STORED row, then the run detail the API returns.
/// </summary>
public sealed class PipelineRunFailureShapeTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000e1");
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    // The host edge the script talks to. Like the real bridge it honours the call's token, waits like a slow
    // TTS provider, and reports a refused call through last.error (null answer, reason set).
    private sealed class ScriptedBridge(int waitMs, params string[] failingKeys) : IScriptHostBridge
    {
        private ScriptHostError? _lastError;

        public string? LastErrorMessage => _lastError?.Message;

        public HostImportDelegate Resolve(string capabilityKey) =>
            capabilityKey == ScriptHostErrorCodes.LastErrorKey
                ? (_, _, _) => _lastError?.ToJson()
                : (key, _, ct) =>
                {
                    _lastError = null;
                    Task.Delay(waitMs, ct).GetAwaiter().GetResult();
                    if (!failingKeys.Contains(key))
                        return "ok";
                    _lastError = new(ScriptHostErrorCodes.UpstreamFailed, "provider is down");
                    return null;
                };
    }

    // An action that stops the run from outside, the way a channel going offline does.
    private sealed class CancelFromOutsideAction(CancellationTokenSource source) : ICommandAction
    {
        public string ActionType => "cancel_from_outside";

        public Application.Abstractions.Localization.LocalizedText Category => new("test.category");

        public Application.Abstractions.Localization.LocalizedText Description => new("test.desc");

        public IReadOnlyList<PipelineActionFieldDescriptor> Fields => [];

        public async Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        )
        {
            await source.CancelAsync();
            await Task.Delay(Timeout.Infinite, ctx.CancellationToken);
            return ActionResult.Success();
        }
    }

    private static (PipelineEngine Engine, PipelineTreeExecutionTestDbContext Db) Build(
        string[]? grantedKeys = null,
        ScriptResourceBudget? budget = null,
        IScriptHostBridge? bridge = null,
        ICommandAction? extraAction = null
    )
    {
        PipelineTreeExecutionTestDbContext db = PipelineTreeExecutionTestDbContext.New();
        FakeTimeProvider time = new(Now);

        IScriptCapabilityBroker broker = Substitute.For<IScriptCapabilityBroker>();
        broker
            .BuildGrantAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new ScriptCapabilityGrant(
                        Channel,
                        [
                            .. (grantedKeys ?? ["tts.speak"]).Select(
                                key => new ScriptCapabilityDescriptor(key, "tos", "ff", true)
                            ),
                        ]
                    )
                )
            );
        IScriptExecutionMeter meter = Substitute.For<IScriptExecutionMeter>();
        meter
            .CheckSandboxBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new QuotaCheck(true, -1, 0, default, default)));
        meter
            .RecordSandboxUsageAsync(
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IScriptHostBridgeFactory bridgeFactory = Substitute.For<IScriptHostBridgeFactory>();
        bridgeFactory
            .Create(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ScriptReplyTarget?>())
            .Returns(bridge ?? new ScriptedBridge(waitMs: 0));

        ScriptRunner runner = new(
            db,
            new JintScriptExecutor(),
            broker,
            meter,
            bridgeFactory,
            time,
            budget
        );

        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult((string)ci[0]));

        List<ICommandAction> actions = [new RunCodeAction(runner)];
        if (extraAction is not null)
            actions.Add(extraAction);

        return (
            new(db, registry, actions, [], resolver, NullLogger<PipelineEngine>.Instance, time),
            db
        );
    }

    private static async Task<string> SeedScriptAsync(
        PipelineTreeExecutionTestDbContext db,
        string js
    )
    {
        CodeScript script = new()
        {
            BroadcasterId = Channel,
            Name = "hug",
            Language = "typescript",
            IsEnabled = true,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        db.CodeScripts.Add(script);
        CodeScriptVersion version = new()
        {
            CodeScriptId = script.Id,
            BroadcasterId = Channel,
            Version = 1,
            SourceCode = js,
            CompiledJs = js,
            CompiledHash = "h",
            ValidationStatus = "valid",
            CreatedAt = Now.UtcDateTime,
        };
        db.CodeScriptVersions.Add(version);
        script.CurrentVersionId = version.Id;
        await db.SaveChangesAsync();
        // run_code takes the owned id in its wire form (a ULID), the way the editor saves it.
        return OwnedIdCodec.Encode(script.Id);
    }

    private static PipelineRequest RunCodeRequest(string codeScriptId) =>
        new()
        {
            BroadcasterId = Channel,
            PipelineJson =
                "{\"steps\":[{\"action\":{\"type\":\"run_code\",\"code_script_id\":\""
                + codeScriptId
                + "\"}}]}",
            TriggeredByUserId = Guid.NewGuid().ToString(),
            TriggeredByDisplayName = "viewer1",
            RawMessage = "!hug",
        };

    private static async Task<PipelineExecutionDetailDto> ReadDetailAsync(
        PipelineTreeExecutionTestDbContext db,
        PipelineExecution row
    )
    {
        PipelineExecutionQueryService query = new(db);
        Result<PipelineExecutionDetailDto> detail = await query.GetDetailAsync(
            Channel.ToString(),
            row.Id
        );
        detail.IsSuccess.Should().BeTrue(detail.ErrorMessage);
        return detail.Value;
    }

    private static List<JsonElement> StoredStepLogs(PipelineExecution row) =>
        [.. JsonDocument.Parse(row.StepLogsJson!).RootElement.EnumerateArray()];

    [Fact]
    public async Task A_script_that_runs_out_of_its_time_budget_after_a_slow_host_call_records_Timeout_with_the_limit_and_the_host_call()
    {
        (PipelineEngine engine, PipelineTreeExecutionTestDbContext db) = Build(
            budget: ScriptResourceBudget.Baseline with
            {
                WallClockMs = 1000,
                MaxFuelOrStatements = int.MaxValue,
            },
            bridge: new ScriptedBridge(waitMs: 300)
        );
        string id = await SeedScriptAsync(
            db,
            "bot.call('tts.speak', 'hi'); bot.send('spoke'); while (true) {}"
        );

        PipelineExecutionResult result = await engine.ExecuteAsync(RunCodeRequest(id));

        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);
        PipelineExecution row = await db.PipelineExecutions.AsNoTracking().SingleAsync();
        row.FailureKind.Should().Be(RunFailureKind.Timeout);
        row.LimitKey.Should().Be("script.wall_clock_ms");
        row.LimitValue.Should().Be(1000);
        row.HostCallCount.Should().Be(1);
        JsonElement step = StoredStepLogs(row).Single();
        JsonElement call = step.GetProperty("HostCalls").EnumerateArray().Single();
        call.GetProperty("Name").GetString().Should().Be("tts.speak");
        call.GetProperty("DurationMs").GetInt32().Should().BeGreaterThanOrEqualTo(250);
        call.GetProperty("Ok").GetBoolean().Should().BeTrue();

        PipelineExecutionDetailDto detail = await ReadDetailAsync(db, row);
        detail.FailureKind.Should().Be(RunFailureKind.Timeout);
        detail.LimitKey.Should().Be("script.wall_clock_ms");
        detail.LimitValue.Should().Be(1000);
        detail.HostCallCount.Should().Be(1);
        PipelineExecutionHostCallDto dtoCall = detail.StepLogs.Single().HostCalls.Single();
        dtoCall.Name.Should().Be("tts.speak");
        dtoCall.DurationMs.Should().BeGreaterThanOrEqualTo(250);
        dtoCall.Ok.Should().BeTrue();
        dtoCall.Error.Should().BeNull();

        // The run list (channel history and the admin diagnostics runs section) carries the same shape.
        Result<PagedList<PipelineExecutionSummaryDto>> list =
            await new PipelineExecutionQueryService(db).ListAsync(
                Channel.ToString(),
                new(),
                failuresOnly: true
            );
        PipelineExecutionSummaryDto summary = list.Value.Items.Single();
        summary.FailureKind.Should().Be(RunFailureKind.Timeout);
        summary.LimitKey.Should().Be("script.wall_clock_ms");
        summary.LimitValue.Should().Be(1000);
    }

    [Fact]
    public async Task A_script_that_throws_records_ScriptError_and_no_limit()
    {
        (PipelineEngine engine, PipelineTreeExecutionTestDbContext db) = Build();
        string id = await SeedScriptAsync(db, "throw new Error('boom');");

        PipelineExecutionResult result = await engine.ExecuteAsync(RunCodeRequest(id));

        result.Outcome.Should().Be(PipelineOutcome.PartiallyFailed);
        PipelineExecution row = await db.PipelineExecutions.AsNoTracking().SingleAsync();
        row.FailureKind.Should().Be(RunFailureKind.ScriptError);
        row.LimitKey.Should().BeNull();
        row.LimitValue.Should().BeNull();
        row.HostCallCount.Should().Be(0);

        PipelineExecutionDetailDto detail = await ReadDetailAsync(db, row);
        detail.FailureKind.Should().Be(RunFailureKind.ScriptError);
        detail.LimitKey.Should().BeNull();
        detail.LimitValue.Should().BeNull();
        detail.StepLogs.Single().HostCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_script_that_makes_more_host_calls_than_its_budget_records_Limit_with_the_budget()
    {
        (PipelineEngine engine, PipelineTreeExecutionTestDbContext db) = Build(
            budget: ScriptResourceBudget.Baseline with
            {
                MaxHostCalls = 2,
            }
        );
        string id = await SeedScriptAsync(
            db,
            "bot.call('tts.speak', 'a'); bot.call('tts.speak', 'b'); bot.call('tts.speak', 'c');"
        );

        await engine.ExecuteAsync(RunCodeRequest(id));

        PipelineExecution row = await db.PipelineExecutions.AsNoTracking().SingleAsync();
        row.FailureKind.Should().Be(RunFailureKind.Limit);
        row.LimitKey.Should().Be("script.max_host_calls");
        row.LimitValue.Should().Be(2);
        row.HostCallCount.Should().Be(2);
    }

    [Fact]
    public async Task A_script_that_asks_for_a_capability_it_was_not_granted_records_Permission()
    {
        (PipelineEngine engine, PipelineTreeExecutionTestDbContext db) = Build(
            grantedKeys: ["chat.send"]
        );
        string id = await SeedScriptAsync(db, "bot.call('tts.speak', 'hi');");

        await engine.ExecuteAsync(RunCodeRequest(id));

        PipelineExecution row = await db.PipelineExecutions.AsNoTracking().SingleAsync();
        row.FailureKind.Should().Be(RunFailureKind.Permission);
        row.LimitKey.Should().BeNull();
        row.HostCallCount.Should().Be(0);
    }

    [Fact]
    public async Task A_successful_run_records_no_failure_and_its_host_calls_including_a_refused_one()
    {
        (PipelineEngine engine, PipelineTreeExecutionTestDbContext db) = Build(
            grantedKeys: ["tts.speak", "chat.send"],
            bridge: new ScriptedBridge(waitMs: 50, failingKeys: "chat.send")
        );
        string id = await SeedScriptAsync(
            db,
            "bot.call('tts.speak', 'hi'); bot.call('chat.send', 'hello'); bot.send('done');"
        );

        PipelineExecutionResult result = await engine.ExecuteAsync(RunCodeRequest(id));

        result.Outcome.Should().Be(PipelineOutcome.Completed, result.StepLogs[^1].ErrorMessage);
        PipelineExecution row = await db.PipelineExecutions.AsNoTracking().SingleAsync();
        row.Status.Should().Be("completed");
        row.FailureKind.Should().BeNull();
        row.LimitKey.Should().BeNull();
        row.LimitValue.Should().BeNull();
        row.HostCallCount.Should().Be(2);

        PipelineExecutionDetailDto detail = await ReadDetailAsync(db, row);
        detail.FailureKind.Should().BeNull();
        detail
            .StepLogs.Single()
            .HostCalls.Select(c => (c.Name, c.Ok, c.Error))
            .Should()
            .Equal(("tts.speak", true, null), ("chat.send", false, "provider is down"));
        detail.StepLogs.Single().HostCalls.Should().OnlyContain(c => c.DurationMs >= 40);
    }

    [Fact]
    public async Task A_run_stopped_from_outside_records_Canceled()
    {
        using CancellationTokenSource source = new();
        (PipelineEngine engine, PipelineTreeExecutionTestDbContext db) = Build(
            extraAction: new CancelFromOutsideAction(source)
        );

        PipelineExecutionResult result = await engine.ExecuteAsync(
            new()
            {
                BroadcasterId = Channel,
                PipelineJson = "{\"steps\":[{\"action\":{\"type\":\"cancel_from_outside\"}}]}",
                TriggeredByUserId = Guid.NewGuid().ToString(),
                TriggeredByDisplayName = "viewer1",
            },
            source.Token
        );

        result.Outcome.Should().Be(PipelineOutcome.Cancelled);
        PipelineExecution row = await db.PipelineExecutions.AsNoTracking().SingleAsync();
        row.FailureKind.Should().Be(RunFailureKind.Canceled);
        row.LimitKey.Should().BeNull();
        row.LimitValue.Should().BeNull();
    }
}
