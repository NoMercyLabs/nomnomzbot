// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.MassBan;
using NomNomzBot.Infrastructure.Platform.Security;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation.MassBan;

/// <summary>
/// A moderator, the channels they moderate on Twitch, which of those are live, and which use the bot — wired to a
/// real SQLite store and to substitutes for Twitch and chat.
/// </summary>
internal sealed class MassBanTestWorld
{
    public static readonly Guid Operator = Guid.NewGuid();

    private readonly List<TwitchModeratedChannel> _moderated = [];
    private readonly HashSet<string> _live = new(StringComparer.Ordinal);

    public MassBanTestWorld()
    {
        Access
            .ResolveOwnChannelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(OwnChannelId);
        Moderators
            .GetModeratedChannelsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Result.Success(
                    new TwitchPage<TwitchModeratedChannel>(
                        [.. _moderated],
                        NextCursor: null,
                        Total: _moderated.Count
                    )
                )
            );
        Streams
            .GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                IReadOnlyList<string> asked = call.Arg<TwitchStreamsFilter>().UserIds ?? [];
                List<TwitchStream> streams = [.. asked.Where(_live.Contains).Select(Stream)];
                return Result.Success(new TwitchPage<TwitchStream>(streams, null, streams.Count));
            });
        Moderation
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success(
                    new TwitchBanResult(
                        call.ArgAt<string>(1),
                        "mod",
                        call.ArgAt<string>(2),
                        DateTimeOffset.UnixEpoch,
                        null
                    )
                )
            );
        Moderation
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Result.Success(
                    new TwitchBanResult(
                        call.ArgAt<Guid>(0).ToString(),
                        "owner",
                        call.ArgAt<string>(1),
                        DateTimeOffset.UnixEpoch,
                        null
                    )
                )
            );
        Composer
            .ComposeAsync(Arg.Any<BuiltinResponseRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => "line:" + call.Arg<BuiltinResponseRequest>().Slot);
        Chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    public Guid OwnChannelId { get; } = Guid.NewGuid();
    public ModerationServiceTestDbContext Db { get; } = ModerationServiceTestDbContext.New();
    public IChannelAccessService Access { get; } = Substitute.For<IChannelAccessService>();
    public ITwitchModeratorsApi Moderators { get; } = Substitute.For<ITwitchModeratorsApi>();
    public ITwitchStreamsApi Streams { get; } = Substitute.For<ITwitchStreamsApi>();
    public ITwitchModerationApi Moderation { get; } = Substitute.For<ITwitchModerationApi>();
    public IBuiltinResponseComposer Composer { get; } = Substitute.For<IBuiltinResponseComposer>();
    public IChatProvider Chat { get; } = Substitute.For<IChatProvider>();
    public OutboundSanctionAccessor Sanctions { get; } = new();
    public FakeTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 10, 5, 22, 0, 0, TimeSpan.Zero));

    /// <summary>The moderator's own channel, served by the bot.</summary>
    public Guid OwnChannel(string twitchId, string login, bool live = false) =>
        AddServed(OwnChannelId, twitchId, login, live, accepts: true);

    /// <summary>A moderated channel that uses the bot.</summary>
    public Guid ServedChannel(
        string twitchId,
        string login,
        bool live = false,
        bool accepts = true
    ) => AddServed(Guid.NewGuid(), twitchId, login, live, accepts);

    /// <summary>A moderated channel that never joined the bot.</summary>
    public void UnservedChannel(string twitchId, string login, bool live = false)
    {
        _moderated.Add(new(twitchId, login, login));
        if (live)
            _live.Add(twitchId);
    }

    /// <summary>
    /// A served channel whose owner opted in, where Twitch does NOT list the operator as a moderator. The operator
    /// is on the bot's roster there when <paramref name="onRoster"/>; its bans ride the broadcaster's token.
    /// </summary>
    public Guid OwnerOptedInChannelWithoutTwitchMod(string twitchId, string login, bool onRoster)
    {
        Guid id = Guid.NewGuid();
        Db.Channels.Add(
            new Channel
            {
                Id = id,
                TwitchChannelId = twitchId,
                Name = login,
                NameNormalized = login,
                IsOnboarded = true,
                AcceptsModeratorMassBans = true,
            }
        );
        if (onRoster)
            Db.ChannelModerators.Add(
                new ChannelModerator
                {
                    ChannelId = id,
                    UserId = Operator,
                    GrantedAt = Clock.GetUtcNow().UtcDateTime,
                }
            );
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return id;
    }

    /// <summary>The operator is a platform principal (admin).</summary>
    public void OperatorIsPlatformPrincipal()
    {
        Db.Users.Add(
            new User
            {
                Id = Operator,
                TwitchUserId = "op-twitch",
                Username = "stoney",
                UsernameNormalized = "stoney",
                DisplayName = "Stoney",
                IsPlatformPrincipal = true,
            }
        );
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    /// <summary>The moderator recorded the streamer's permission for a channel they moderate.</summary>
    public void ModeratorOptIn(string twitchId, string login)
    {
        Db.ModeratorMassBanOptIns.Add(
            new ModeratorMassBanOptIn
            {
                OperatorUserId = Operator,
                BroadcasterTwitchId = twitchId,
                BroadcasterLogin = login,
                Note = "asked in Discord",
                RecordedAt = Clock.GetUtcNow().UtcDateTime,
            }
        );
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    public void GoOffline(string twitchId) => _live.Remove(twitchId);

    public MassBanConsentService Consent() =>
        new(
            Planner(),
            Resolver(),
            Db,
            Composer,
            Chat,
            Clock,
            NullLogger<MassBanConsentService>.Instance
        );

    public MassBanExecutor Executor() =>
        new(
            Db,
            Moderation,
            new MassBanLiveChannels(Streams),
            Composer,
            Chat,
            Sanctions,
            Clock,
            NullLogger<MassBanExecutor>.Instance
        );

    private MassBanChannelPlanner Planner() =>
        new(Resolver(), Access, new MassBanLiveChannels(Streams), Db);

    private OperatorModeratedChannelResolver Resolver() => new(Access, Moderators, Db);

    private Guid AddServed(Guid id, string twitchId, string login, bool live, bool accepts)
    {
        _moderated.Add(new(twitchId, login, login));
        if (live)
            _live.Add(twitchId);
        Db.Channels.Add(
            new Channel
            {
                Id = id,
                TwitchChannelId = twitchId,
                Name = login,
                NameNormalized = login,
                IsOnboarded = true,
                AcceptsModeratorMassBans = accepts,
            }
        );
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return id;
    }

    private static TwitchStream Stream(string userId) =>
        new(
            "s" + userId,
            userId,
            userId,
            userId,
            "",
            "",
            "live",
            "",
            [],
            1,
            DateTimeOffset.UnixEpoch,
            "en",
            "",
            false
        );
}
