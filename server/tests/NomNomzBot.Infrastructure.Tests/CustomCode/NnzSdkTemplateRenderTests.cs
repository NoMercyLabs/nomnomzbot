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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Services;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// <c>nnz.template.render(template, values?)</c> renders a template through the one resolver for the script's
/// channel. These tests run the real script engine, the real bridge dispatch and the real resolver with a fake
/// channel, and assert the exact text.
/// </summary>
public sealed class NnzSdkTemplateRenderTests
{
    private const string Capability = "template.render";
    private static readonly Guid Channel = Guid.Parse("00000000-0000-0000-0000-00000000000a");

    private static IScriptHostBridge RealBridge()
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "123456",
                    ChannelName = "samplechannel",
                    DisplayName = "SampleChannel",
                    CommandPrefix = "!",
                }
            );
        ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        TemplateResolver resolver = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System,
            services.GetRequiredService<ITemplateHelperRegistry>()
        );

        return new ScriptHostBridgeFactory(
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
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<ISevenTvUserPaintResolver>(),
            Substitute.For<IOwnerActionService>(),
            Substitute.For<ITwitchUsersApi>(),
            Substitute.For<IMentionTargetResolver>(),
            Substitute.For<IStreamStatsService>(),
            resolver
        ).Create(Channel, "u1", null);
    }

    private static async Task<ScriptExecutionOutcomeResult> Run(string js)
    {
        ScriptExecutionRequest request = new(
            "exec-1",
            js,
            "hash",
            new("u1", "User", [], new Dictionary<string, string>()),
            ScriptResourceBudget.Baseline with
            {
                WallClockMs = 30_000,
            }
        );
        ScriptCapabilityGrant grant = new(Channel, [new(Capability, "low", "custom_code", false)]);
        return (await new JintScriptExecutor().ExecuteAsync(request, grant, RealBridge())).Value;
    }

    [Fact]
    public async Task A_helper_in_the_template_renders_to_its_text()
    {
        ScriptExecutionOutcomeResult result = await Run(
            "bot.setVar('r', nnz.template.render('{text.upper:hi}'));"
        );

        result.Outcome.Should().Be(ScriptExecutionOutcome.Success, result.ErrorMessage);
        result.VariablesOut["r"].Should().Be("HI");
    }

    [Fact]
    public async Task Values_fill_the_names_of_the_template_and_numbers_and_booleans_become_text()
    {
        ScriptExecutionOutcomeResult result = await Run(
            "bot.setVar('r', nnz.template.render('{greeting} {text.upper:{name}} {count} {flag}',"
                + " { greeting: 'hey', name: 'bob', count: 3, flag: true }));"
        );

        result.Outcome.Should().Be(ScriptExecutionOutcome.Success, result.ErrorMessage);
        result.VariablesOut["r"].Should().Be("hey BOB 3 true");
    }

    [Fact]
    public async Task A_template_with_no_text_stops_the_script_with_an_error_that_names_the_call()
    {
        ScriptExecutionOutcomeResult result = await Run("nnz.template.render();");

        result.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
        result.ErrorMessage.Should().Contain("nnz.template.render needs a template");
    }

    [Fact]
    public async Task A_template_over_the_size_limit_gives_empty_text_and_sets_the_last_error()
    {
        ScriptExecutionOutcomeResult result = await Run(
            "bot.setVar('r', nnz.template.render('x'.repeat(5000)));"
                + "bot.setVar('code', nnz.lastError.code);"
        );

        result.Outcome.Should().Be(ScriptExecutionOutcome.Success, result.ErrorMessage);
        result.VariablesOut["r"].Should().BeEmpty();
        result.VariablesOut["code"].Should().Be("invalid_argument");
    }

    [Fact]
    public async Task The_call_is_declared_by_the_save_time_capability_scan()
    {
        Result<ScriptCompilation> compile = await new JintScriptExecutor().CompileAsync(
            "nnz.template.render('{text.upper:a}');"
        );

        compile.Value.DeclaredCapabilities.Should().Contain(Capability);
    }
}
