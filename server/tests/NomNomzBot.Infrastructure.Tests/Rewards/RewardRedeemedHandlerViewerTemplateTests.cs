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

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A viewer's reward input through the real redemption handler, the real viewer-input service and the real
/// template resolver: the channel flag and the per-viewer action decide whether the typed text is resolved,
/// and the spoken or stored text is asserted, not a mock call.
/// </summary>
public sealed class RewardRedeemedHandlerViewerTemplateTests
{
    private const string Typed = "{text.repeat:30|ha}";
    private static readonly string Cap20 = string.Concat(Enumerable.Repeat("ha", 20));

    private const string OtherViewerTwitchId = "twitch-viewer-2";

    private static readonly Guid OtherViewerUserId = Guid.Parse(
        "0192a000-0000-7000-8000-00000000c404"
    );

    private sealed record Setup(
        RewardRedeemedHandlerTests.Harness H,
        RecordingEventBus Bus,
        IRoleResolver Roles
    );

    /// <param name="flagOn">The channel's <c>use_viewer_templates</c> feature.</param>
    /// <param name="permitted">The users who hold <c>reward:input:template</c>; everyone else does not.</param>
    private static async Task<Setup> BuildAsync(
        bool flagOn,
        bool speaksViaTts,
        params Guid[] permitted
    )
    {
        RecordingEventBus bus = new();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(0));
        roles
            .HasCapabilityAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                ViewerTemplateCapabilities.RewardInput,
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Result.Success(permitted.Contains(call.ArgAt<Guid>(0))));

        IFeatureService features = Substitute.For<IFeatureService>();
        features
            .IsFeatureEnabledAsync(
                Arg.Any<string>(),
                ViewerTemplateCapabilities.FeatureKey,
                Arg.Any<CancellationToken>()
            )
            .Returns(flagOn);

        ITtsConfigService ttsConfig = Substitute.For<ITtsConfigService>();
        ttsConfig
            .GetConfigAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsConfigDto(
                        true,
                        "chat",
                        "azure",
                        null,
                        500,
                        "everyone",
                        false,
                        false,
                        false,
                        false,
                        null,
                        true,
                        false,
                        false,
                        null,
                        false,
                        0
                    )
                )
            );
        IBillingTierService tiers = Substitute.For<IBillingTierService>();
        tiers
            .GetLimitAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(-1L));

        ServiceProvider templateServices = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        TemplateResolver resolver = TextHelpersTableTests.BuildResolver(
            templateServices.GetRequiredService<ITemplateHelperRegistry>()
        );

        RewardRedeemedHandlerTests.Harness h = RewardRedeemedHandlerTests.Build(
            configure: services =>
                services
                    .AddLogging()
                    .AddSingleton(roles)
                    .AddSingleton(features)
                    .AddSingleton(ttsConfig)
                    .AddSingleton(tiers)
                    .AddSingleton<IEventBus>(bus)
                    .AddSingleton<IViewerTemplateResolver>(resolver)
                    .AddSingleton<IViewerInputTemplateService, ViewerInputTemplateService>()
        );
        h.Db.Users.Add(
            new()
            {
                Id = OtherViewerUserId,
                TwitchUserId = OtherViewerTwitchId,
                Username = "other",
                UsernameNormalized = "other",
                DisplayName = "Other",
            }
        );
        h.Db.Rewards.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = RewardRedeemedHandlerTests.Channel,
                Title = "Say it",
                TwitchRewardId = "tw-tpl-1",
                SpeaksViaTts = speaksViaTts,
            }
        );
        await h.Db.SaveChangesAsync();
        return new(h, bus, roles);
    }

    private static RewardRedeemedEvent Redemption(string input, string viewerTwitchId) =>
        new()
        {
            BroadcasterId = RewardRedeemedHandlerTests.Channel,
            RewardId = "tw-tpl-1",
            RewardTitle = "Say it",
            RedemptionId = "redemption-tpl-1",
            UserId = viewerTwitchId,
            UserDisplayName = "Viewer",
            Cost = 100,
            UserInput = input,
        };

    private static string SpokenText(Setup s)
    {
        TtsSpeakRequest request = s
            .H.Tts.ReceivedCalls()
            .Where(call =>
                call.GetMethodInfo().Name == nameof(ITtsDispatchService.RequestSpeakAsync)
            )
            .Select(call => (TtsSpeakRequest)call.GetArguments()[0]!)
            .Single();
        return request.Text;
    }

    [Fact]
    public async Task With_the_flag_off_the_spoken_text_is_exactly_what_the_viewer_typed()
    {
        Setup s = await BuildAsync(
            flagOn: false,
            speaksViaTts: true,
            RewardRedeemedHandlerTests.ViewerUserId
        );

        await s.H.Handler.HandleAsync(Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId));

        SpokenText(s).Should().Be("{text.repeat:30|ha}");
        s.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task With_the_flag_on_a_permitted_subscriber_is_spoken_with_the_copy_cap_applied()
    {
        Setup s = await BuildAsync(
            flagOn: true,
            speaksViaTts: true,
            RewardRedeemedHandlerTests.ViewerUserId
        );

        await s.H.Handler.HandleAsync(Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId));

        SpokenText(s).Should().Be(Cap20).And.HaveLength(40);
        s.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task With_the_flag_on_a_viewer_without_the_action_is_spoken_literally_and_the_refusal_is_published()
    {
        Setup s = await BuildAsync(flagOn: true, speaksViaTts: true);

        await s.H.Handler.HandleAsync(Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId));

        SpokenText(s).Should().Be("{text.repeat:30|ha}");
        ViewerTemplateRefusedEvent refused = s
            .Bus.Published.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ViewerTemplateRefusedEvent>()
            .Subject;
        refused.Reason.Should().Be("no_permission");
        refused.ActionKey.Should().Be("reward:input:template");
        refused.CallerUserId.Should().Be(RewardRedeemedHandlerTests.ViewerUserId);
        refused.InputLength.Should().Be(Typed.Length);
        refused.BroadcasterId.Should().Be(RewardRedeemedHandlerTests.Channel);
    }

    [Fact]
    public async Task One_permitted_viewer_resolves_while_another_viewer_in_the_same_channel_stays_literal()
    {
        Setup s = await BuildAsync(flagOn: true, speaksViaTts: true, OtherViewerUserId);

        await s.H.Handler.HandleAsync(Redemption(Typed, OtherViewerTwitchId));
        SpokenText(s).Should().Be(Cap20);
        s.Bus.Published.Should().BeEmpty();

        s.H.Tts.ClearReceivedCalls();
        await s.H.Handler.HandleAsync(Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId));
        SpokenText(s).Should().Be("{text.repeat:30|ha}");
        s.Bus.Published.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ViewerTemplateRefusedEvent>();
    }

    [Fact]
    public async Task A_helper_that_writes_or_includes_stays_as_written_inside_a_resolved_text()
    {
        Setup s = await BuildAsync(
            flagOn: true,
            speaksViaTts: true,
            RewardRedeemedHandlerTests.ViewerUserId
        );

        await s.H.Handler.HandleAsync(
            Redemption(
                "{include.cmd:hug} {count.visits} {text.upper:ok}",
                RewardRedeemedHandlerTests.ViewerTwitchId
            )
        );

        SpokenText(s).Should().Be("{include.cmd:hug} {count.visits} OK");
    }

    [Fact]
    public async Task A_text_nested_past_the_depth_limit_is_spoken_literally_and_the_refusal_is_published()
    {
        Setup s = await BuildAsync(
            flagOn: true,
            speaksViaTts: true,
            RewardRedeemedHandlerTests.ViewerUserId
        );
        const string deep = "{text.upper:{text.reverse:{text.lower:{text.trim: ABC }}}}";

        await s.H.Handler.HandleAsync(Redemption(deep, RewardRedeemedHandlerTests.ViewerTwitchId));

        SpokenText(s).Should().Be(deep);
        s.Bus.Published.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ViewerTemplateRefusedEvent>()
            .Which.Reason.Should()
            .Be("too_deep");
    }

    [Fact]
    public async Task A_reward_input_that_is_not_spoken_resolves_the_same_way_into_the_input_variable()
    {
        Setup s = await BuildAsync(
            flagOn: true,
            speaksViaTts: false,
            RewardRedeemedHandlerTests.ViewerUserId
        );

        await s.H.Handler.HandleAsync(Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId));

        Dictionary<string, string> variables = s
            .H.Executor.ReceivedCalls()
            .Select(call => (Dictionary<string, string>)call.GetArguments()[4]!)
            .Single();
        variables["input"].Should().Be(Cap20);
        await s.H.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }

    [Fact]
    public async Task A_reward_input_that_is_not_spoken_stays_literal_for_a_viewer_without_the_action()
    {
        Setup s = await BuildAsync(flagOn: true, speaksViaTts: false);

        await s.H.Handler.HandleAsync(Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId));

        Dictionary<string, string> variables = s
            .H.Executor.ReceivedCalls()
            .Select(call => (Dictionary<string, string>)call.GetArguments()[4]!)
            .Single();
        variables["input"].Should().Be("{text.repeat:30|ha}");
    }

    [Fact]
    public async Task A_replay_writes_no_audit_event_for_a_refused_viewer_and_still_resolves_for_a_permitted_one()
    {
        Setup refusedViewer = await BuildAsync(flagOn: true, speaksViaTts: true);
        refusedViewer
            .H.Executor.ReplayAsync(default, default!, default, default, default!, default)
            .ReturnsForAnyArgs(EventResponseOutcome.None);

        await refusedViewer.H.Handler.ReplayAsync(
            Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId),
            CancellationToken.None
        );

        SpokenText(refusedViewer).Should().Be("{text.repeat:30|ha}");
        refusedViewer.Bus.Published.Should().BeEmpty();

        Setup permittedViewer = await BuildAsync(
            flagOn: true,
            speaksViaTts: true,
            RewardRedeemedHandlerTests.ViewerUserId
        );
        permittedViewer
            .H.Executor.ReplayAsync(default, default!, default, default, default!, default)
            .ReturnsForAnyArgs(EventResponseOutcome.None);

        await permittedViewer.H.Handler.ReplayAsync(
            Redemption(Typed, RewardRedeemedHandlerTests.ViewerTwitchId),
            CancellationToken.None
        );

        SpokenText(permittedViewer).Should().Be(Cap20);
        permittedViewer.Bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task A_text_without_a_token_never_reads_the_flag_or_the_viewers_rights()
    {
        Setup s = await BuildAsync(flagOn: true, speaksViaTts: true);

        await s.H.Handler.HandleAsync(
            Redemption("just words", RewardRedeemedHandlerTests.ViewerTwitchId)
        );

        SpokenText(s).Should().Be("just words");
        s.Bus.Published.Should().BeEmpty();
        await s
            .Roles.DidNotReceiveWithAnyArgs()
            .HasCapabilityAsync(default, default, default!, default);
    }
}
