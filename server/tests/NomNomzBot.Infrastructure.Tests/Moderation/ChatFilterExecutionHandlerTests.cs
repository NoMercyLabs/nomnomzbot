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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the chat-filter execution path (moderation.md §3.2/§3.11): the handler runs a channel's enabled
/// <c>ChatFilter</c> rules against every incoming message and follows through — an <c>escalate</c> rule records
/// one ladder offense and applies the ladder's decision; a <c>timeout</c> rule times the sender out for its
/// configured duration; a sender at/above the filter's exemption floor is never touched; and a message that
/// matches nothing produces no action at all.
/// </summary>
public sealed class ChatFilterExecutionHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid SubjectUserId = Guid.Parse("0192a000-0000-7000-8000-0000000000aa");
    private const string TargetTwitchUserId = "viewer-123";
    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 7, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        ChatFilterExecutionHandler Handler,
        ModerationServiceTestDbContext Db,
        ITwitchModerationApi Moderation,
        ModerationEscalationService Escalation
    );

    private static Harness Build()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();

        IUserService users = Substitute.For<IUserService>();
        users
            .GetOrCreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new UserDto(
                        SubjectUserId.ToString(),
                        "viewer",
                        "Viewer",
                        null,
                        null,
                        default,
                        default
                    )
                )
            );

        ModerationEscalationService escalation = new(db, new FakeTimeProvider(T0));
        ChatFilterExecutionHandler handler = new(
            db,
            moderation,
            escalation,
            users,
            NullLogger<ChatFilterExecutionHandler>.Instance
        );
        return new Harness(handler, db, moderation, escalation);
    }

    private static ChatMessageReceivedEvent Message(string text, bool isVip = false) =>
        new()
        {
            MessageId = "msg-1",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "tw-chan",
            UserId = TargetTwitchUserId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = isVip,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static async Task<ChatFilter> SeedBlocklistFilter(
        ModerationServiceTestDbContext db,
        ChatFilterAction action,
        List<string> terms,
        int? timeoutSeconds = null,
        int exemptMinRoleLevel = 10
    )
    {
        ChatFilter filter = new()
        {
            BroadcasterId = Channel,
            FilterType = ChatFilterType.Blocklist,
            Name = "test-filter",
            Action = action,
            TermsJson = System.Text.Json.JsonSerializer.Serialize(terms),
            TimeoutSeconds = timeoutSeconds,
            ExemptMinRoleLevel = exemptMinRoleLevel,
            IsEnabled = true,
        };
        db.ChatFilters.Add(filter);
        await db.SaveChangesAsync();
        return filter;
    }

    [Fact]
    public async Task An_escalate_rule_records_an_offense_and_applies_the_ladder_decision()
    {
        Harness h = Build();
        await h.Escalation.UpsertPolicyAsync(
            Channel,
            new UpsertEscalationPolicyRequest(
                IsEnabled: true,
                Ladder:
                [
                    new EscalationLadderStep(1, "warn", null),
                    new EscalationLadderStep(2, "timeout", 60),
                    new EscalationLadderStep(3, "ban", null),
                ],
                OffenseWindowHours: 168,
                CountAutoModViolations: false
            )
        );
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"]);

        await h.Handler.HandleAsync(Message("this is banned content"));

        // The offense was recorded on the ladder (J.11) for the resolved subject.
        ModerationEscalationState state = await h.Db.ModerationEscalationStates.SingleAsync();
        state.SubjectUserId.Should().Be(SubjectUserId);
        state.OffenseCount.Should().Be(1);

        // First offense → the ladder returns "warn", which is applied via the moderation API.
        await h
            .Moderation.Received(1)
            .WarnChatUserAsync(
                Channel,
                TargetTwitchUserId,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Moderation.DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Moderation.DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );

        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task The_second_escalate_offense_climbs_to_the_next_rung_and_times_out()
    {
        Harness h = Build();
        await h.Escalation.UpsertPolicyAsync(
            Channel,
            new UpsertEscalationPolicyRequest(
                IsEnabled: true,
                Ladder:
                [
                    new EscalationLadderStep(1, "warn", null),
                    new EscalationLadderStep(2, "timeout", 60),
                ],
                OffenseWindowHours: 168,
                CountAutoModViolations: false
            )
        );
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Escalate, ["banned"]);

        await h.Handler.HandleAsync(Message("banned once"));
        await h.Handler.HandleAsync(Message("banned twice"));

        (await h.Db.ModerationEscalationStates.SingleAsync()).OffenseCount.Should().Be(2);
        // Second offense → the ladder returns "timeout 60", applied with the ladder's duration (not the filter's).
        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                60,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_timeout_rule_times_the_sender_out_for_the_configured_duration()
    {
        Harness h = Build();
        await SeedBlocklistFilter(h.Db, ChatFilterAction.Timeout, ["spam"], timeoutSeconds: 300);

        await h.Handler.HandleAsync(Message("buy cheap spam now"));

        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                300,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task An_exempt_sender_is_left_untouched()
    {
        Harness h = Build();
        // Exempt at the VIP rung (level 4); the sender is a VIP, so the filter must skip them.
        await SeedBlocklistFilter(
            h.Db,
            ChatFilterAction.Timeout,
            ["spam"],
            timeoutSeconds: 300,
            exemptMinRoleLevel: 4
        );

        await h.Handler.HandleAsync(Message("spam spam spam", isVip: true));

        await h
            .Moderation.DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
    }

    [Fact]
    public async Task A_non_matching_message_produces_no_action()
    {
        Harness h = Build();
        await SeedBlocklistFilter(
            h.Db,
            ChatFilterAction.Timeout,
            ["forbidden"],
            timeoutSeconds: 300
        );

        await h.Handler.HandleAsync(Message("hello everyone, lovely stream"));

        await h
            .Moderation.DidNotReceive()
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await h
            .Moderation.DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(0);
        (await h.Db.ModerationEscalationStates.CountAsync()).Should().Be(0);
    }
}
