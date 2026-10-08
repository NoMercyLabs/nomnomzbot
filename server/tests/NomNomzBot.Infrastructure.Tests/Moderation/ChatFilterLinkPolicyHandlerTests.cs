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
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves a <see cref="ChatFilterType.LinkPolicy"/> filter reads its stored policy: a link on an allowed domain
/// (or any sub-domain of it) is left alone, a link anywhere else follows through with the filter's action and
/// counts the match, a bare <c>example.com</c> counts only when the policy asks for it, and a filter with no
/// policy keeps tripping on every link as before.
/// </summary>
public sealed class ChatFilterLinkPolicyHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly Guid SubjectUserId = Guid.Parse("0192a000-0000-7000-8000-0000000000aa");
    private const string TargetTwitchUserId = "viewer-123";
    private static readonly DateTimeOffset T0 = new(2026, 7, 17, 7, 0, 0, TimeSpan.Zero);

    private const string AllowExampleJson = """{"allowedDomains":["example.com"]}""";
    private const string AllowExampleAndBareJson =
        """{"allowedDomains":["example.com"],"matchBareDomains":true}""";

    private sealed record Harness(
        ChatFilterExecutionHandler Handler,
        EventStoreTestDbContext Db,
        ITwitchModerationApi Moderation
    );

    private static Harness Build()
    {
        SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        moderation
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(new TwitchBanResult("tw-chan", "mod", TargetTwitchUserId, T0, null))
            );

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
        ModerationQueueService queue = new(
            db,
            users,
            moderation,
            Substitute.For<IModerationService>(),
            Substitute.For<IEventBus>(),
            new FakeTimeProvider(T0),
            NullLogger<ModerationQueueService>.Instance
        );
        ChatFilterExecutionHandler handler = new(
            db,
            moderation,
            escalation,
            queue,
            users,
            NomNomzBot.Infrastructure.Tests.Platform.Security.TestSanction.Held(),
            new RecordingEventBus(),
            NullLogger<ChatFilterExecutionHandler>.Instance
        );
        return new(handler, db, moderation);
    }

    private static ChatMessageReceivedEvent Message(string text) =>
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
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static async Task SeedLinkFilter(EventStoreTestDbContext db, string? linkPolicyJson)
    {
        db.ChatFilters.Add(
            new()
            {
                BroadcasterId = Channel,
                FilterType = ChatFilterType.LinkPolicy,
                Name = "links",
                Action = ChatFilterAction.Timeout,
                TimeoutSeconds = 120,
                LinkPolicyJson = linkPolicyJson,
                ExemptMinRoleLevel = 10,
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task AssertTimedOut(Harness h)
    {
        await h
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                TargetTwitchUserId,
                120,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    private static async Task AssertLeftAlone(Harness h)
    {
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
    public async Task A_link_on_an_allowed_domain_is_left_alone()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleJson);

        await h.Handler.HandleAsync(Message("my site https://example.com/about is up"));

        await AssertLeftAlone(h);
    }

    [Fact]
    public async Task A_link_on_a_sub_domain_of_an_allowed_domain_is_left_alone()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleJson);

        await h.Handler.HandleAsync(Message("docs at https://blog.shop.example.com/post"));

        await AssertLeftAlone(h);
    }

    [Fact]
    public async Task A_link_on_a_domain_that_only_ends_in_the_allowed_name_still_trips()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleJson);

        await h.Handler.HandleAsync(Message("click https://evilexample.com/x"));

        await AssertTimedOut(h);
    }

    [Fact]
    public async Task A_link_to_a_domain_that_is_not_allowed_times_the_sender_out_and_counts_the_match()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleJson);

        await h.Handler.HandleAsync(Message("free bits https://spam.example.net/win"));

        await AssertTimedOut(h);
    }

    [Fact]
    public async Task One_disallowed_link_among_allowed_ones_trips()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleJson);

        await h.Handler.HandleAsync(
            Message("see https://example.com/a and also https://spam.example.net/b")
        );

        await AssertTimedOut(h);
    }

    [Fact]
    public async Task A_bare_domain_is_ignored_unless_the_policy_asks_for_bare_domains()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleJson);

        await h.Handler.HandleAsync(Message("go to spam-site.net now"));

        await AssertLeftAlone(h);
    }

    [Fact]
    public async Task A_disallowed_bare_domain_trips_when_the_policy_asks_for_bare_domains()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleAndBareJson);

        await h.Handler.HandleAsync(Message("go to spam-site.net now"));

        await AssertTimedOut(h);
    }

    [Fact]
    public async Task An_allowed_bare_domain_is_left_alone_when_the_policy_asks_for_bare_domains()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleAndBareJson);

        await h.Handler.HandleAsync(Message("go to www.example.com now"));

        await AssertLeftAlone(h);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json at all")]
    public async Task A_filter_with_no_usable_policy_still_trips_on_every_link(string? policyJson)
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, policyJson);

        await h.Handler.HandleAsync(Message("look https://example.com/about"));

        await AssertTimedOut(h);
    }

    [Fact]
    public async Task A_message_without_any_link_never_trips()
    {
        Harness h = Build();
        await SeedLinkFilter(h.Db, AllowExampleAndBareJson);

        await h.Handler.HandleAsync(Message("hello everyone, version v1.2 is out"));

        await AssertLeftAlone(h);
    }
}
