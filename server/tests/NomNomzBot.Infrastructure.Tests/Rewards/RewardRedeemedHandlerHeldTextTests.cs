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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Rewards.Events;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Rewards;

/// <summary>
/// A "speaks via TTS" reward through the real redemption handler: text the held-text check reports as held
/// (AutoMod holds it, or a moderator denied it) is never handed to the TTS dispatch. The check itself runs
/// against a real queue table in <c>HeldTextServiceTests</c>; this harness' database context has no queue
/// table, so the check is the one faked part here, and the call it receives is asserted.
/// </summary>
public sealed class RewardRedeemedHandlerHeldTextTests
{
    private const string Typed = "Ebola Ebola bola";

    private static async Task<(
        RewardRedeemedHandlerTests.Harness H,
        IHeldTextService Held
    )> BuildAsync(bool held)
    {
        IHeldTextService heldText = Substitute.For<IHeldTextService>();
        heldText
            .IsTextHeldAsync(
                RewardRedeemedHandlerTests.Channel,
                RewardRedeemedHandlerTests.ViewerTwitchId,
                Typed,
                Arg.Any<CancellationToken>()
            )
            .Returns(held);
        RewardRedeemedHandlerTests.Harness h = RewardRedeemedHandlerTests.Build(
            configure: services => services.AddSingleton(heldText)
        );
        h.Executor.ReplayAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<Dictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(EventResponseOutcome.None);
        h.Db.Rewards.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = RewardRedeemedHandlerTests.Channel,
                Title = "Text-to-Speech Message",
                TwitchRewardId = "tw-held-1",
                SpeaksViaTts = true,
            }
        );
        await h.Db.SaveChangesAsync();
        return (h, heldText);
    }

    private static RewardRedeemedEvent Redemption() =>
        new()
        {
            BroadcasterId = RewardRedeemedHandlerTests.Channel,
            RewardId = "tw-held-1",
            RewardTitle = "Text-to-Speech Message",
            RedemptionId = "redemption-held-1",
            UserId = RewardRedeemedHandlerTests.ViewerTwitchId,
            UserDisplayName = "Viewer",
            Cost = 100,
            UserInput = Typed,
        };

    private static List<TtsSpeakRequest> Spoken(RewardRedeemedHandlerTests.Harness h) =>
        h
            .Tts.ReceivedCalls()
            .Where(call =>
                call.GetMethodInfo().Name == nameof(ITtsDispatchService.RequestSpeakAsync)
            )
            .Select(call => (TtsSpeakRequest)call.GetArguments()[0]!)
            .ToList();

    [Fact]
    public async Task A_held_text_is_not_spoken_and_the_outcome_counts_no_speech()
    {
        (RewardRedeemedHandlerTests.Harness h, IHeldTextService held) = await BuildAsync(true);

        EventResponseOutcome outcome = await h.Handler.ReplayAsync(
            Redemption(),
            CancellationToken.None
        );

        Spoken(h).Should().BeEmpty();
        outcome.TtsQueued.Should().Be(0);
        await held.Received(1)
            .IsTextHeldAsync(
                RewardRedeemedHandlerTests.Channel,
                RewardRedeemedHandlerTests.ViewerTwitchId,
                Typed,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_text_that_is_not_held_is_spoken_exactly_as_typed()
    {
        (RewardRedeemedHandlerTests.Harness h, IHeldTextService held) = await BuildAsync(false);

        EventResponseOutcome outcome = await h.Handler.ReplayAsync(
            Redemption(),
            CancellationToken.None
        );

        TtsSpeakRequest request = Spoken(h).Should().ContainSingle().Subject;
        request.Text.Should().Be(Typed);
        request.RequestedByTwitchUserId.Should().Be(RewardRedeemedHandlerTests.ViewerTwitchId);
        outcome.TtsQueued.Should().Be(1);
        await held.Received(1)
            .IsTextHeldAsync(
                RewardRedeemedHandlerTests.Channel,
                RewardRedeemedHandlerTests.ViewerTwitchId,
                Typed,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_live_redemption_with_a_held_text_is_not_spoken_either()
    {
        (RewardRedeemedHandlerTests.Harness h, _) = await BuildAsync(true);

        await h.Handler.HandleAsync(Redemption());

        Spoken(h).Should().BeEmpty();
    }
}
