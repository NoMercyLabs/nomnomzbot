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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the SDK promise "nnz.api.widget.emit returns false when the send fails": a throwing overlay notifier
/// does not fault the script, it returns false and fills <c>nnz.lastError</c> — while the run's own cancellation
/// still stops the script.
/// </summary>
public sealed class ScriptHostBridgeWidgetEmitFailureTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly Guid WidgetId = Guid.Parse("0192a000-0000-7000-8000-00000000e0c1");

    private static ScriptHostBridge Build(IWidgetEventNotifier notifier)
    {
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new WidgetDetail(
                        Id: WidgetId,
                        Name: "Alert Box",
                        Description: null,
                        Framework: "vue",
                        Source: "custom",
                        IsEnabled: true,
                        OverlayUrl: null,
                        ActiveVersionId: null,
                        GalleryItemId: null,
                        Settings: new(),
                        EventSubscriptions: [],
                        LastRuntimeError: null,
                        LastRanAt: null,
                        CreatedAt: DateTime.UtcNow,
                        UpdatedAt: DateTime.UtcNow,
                        GalleryUpdateAvailable: false,
                        IsAttached: false,
                        IsCustomized: false
                    )
                )
            );
        return new(
            Channel,
            Guid.NewGuid().ToString(),
            null,
            Substitute.For<IChatProvider>(),
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            Substitute.For<IScriptStorageService>(),
            Substitute.For<ITtsDispatchService>(),
            widgets,
            notifier,
            Substitute.For<IRewardService>(),
            Substitute.For<IViewerAnalyticsService>(),
            Substitute.For<ITtsConfigService>(),
            Substitute.For<IScheduledPipelineService>(),
            AuthTestBuilder.NewContext(),
            Substitute.For<ISevenTvUserPaintResolver>(),
            Substitute.For<IOwnerActionService>()
        );
    }

    [Fact]
    public async Task A_failed_send_returns_false_sets_last_error_and_the_script_keeps_running()
    {
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();
        notifier
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            )
            .Throws(new InvalidOperationException("overlay hub is down"));
        ScriptHostBridge bridge = Build(notifier);
        ScriptExecutionRequest request = new(
            "exec-1",
            $$"""
            var sent = nnz.api.widget.emit('{{WidgetId}}', 'confetti');
            var e = nnz.lastError;
            bot.setVar('sent', String(sent));
            bot.setVar('code', e ? e.code : 'none');
            bot.setVar('after', 'ran');
            """,
            "hash",
            new("u1", "User", [], new Dictionary<string, string>()),
            ScriptResourceBudget.Baseline with
            {
                WallClockMs = 30_000,
            }
        );
        ScriptCapabilityGrant grant = new(Guid.NewGuid(), [new("widget.emit", "tos", "ff", true)]);

        ScriptExecutionOutcomeResult result = (
            await new JintScriptExecutor().ExecuteAsync(request, grant, bridge)
        ).Value;

        result.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        result.VariablesOut["sent"].Should().Be("false");
        result.VariablesOut["code"].Should().Be("upstream_failed");
        result.VariablesOut["after"].Should().Be("ran");
    }

    [Fact]
    public void The_runs_own_cancellation_still_stops_the_script()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        IWidgetEventNotifier notifier = Substitute.For<IWidgetEventNotifier>();
        notifier
            .SendWidgetEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            )
            .Throws(new OperationCanceledException(cts.Token));
        ScriptHostBridge bridge = Build(notifier);

        Action emit = () =>
            bridge.Resolve("widget.emit")(
                "widget.emit",
                [WidgetId.ToString(), "confetti"],
                cts.Token
            );

        emit.Should().Throw<OperationCanceledException>();
    }
}
