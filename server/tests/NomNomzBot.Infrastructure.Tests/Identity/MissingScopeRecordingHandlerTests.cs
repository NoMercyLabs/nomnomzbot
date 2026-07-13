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
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Identity.EventHandlers;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the missing-scope chat notice is gated on the caller context (identity-auth §3.4a): a gap surfaced
/// while serving a live HTTP/dashboard request is recorded for the dashboard's own missing-scope surface but is
/// NOT announced in the channel's public chat — an operator at the dashboard already sees the inline banner, so
/// the chat line would be redundant noise (and lands in a moderated channel's chat, not the operator's). The
/// autonomous path (no HTTP context — EventSub handlers, timers, background jobs) still announces, since a live
/// chat line is the only way to reach a streamer who isn't looking at the dashboard.
/// </summary>
public sealed class MissingScopeRecordingHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private const string MissingScope = "moderator:read:shield_mode";

    private static (
        MissingScopeRecordingHandler Handler,
        AuthDbContext Db,
        SpyChatProvider Chat
    ) Build(HttpContext? httpContext)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        SpyChatProvider chat = new();
        // A real ScopeNotificationService over the same in-memory context + a spy chat provider, so the test
        // asserts the actual side effect (a chat send, or none) rather than a mock interaction. The service
        // resolves IChatProvider + IPlatformBotReadinessGate lazily, so the provider hands both back.
        IServiceProvider provider = new ServiceCollection()
            .AddSingleton<IChatProvider>(chat)
            .AddSingleton<IPlatformBotReadinessGate>(new StubBotReadiness(true))
            .BuildServiceProvider();
        ScopeNotificationService notifications = new(
            db,
            provider,
            TimeProvider.System,
            NullLogger<ScopeNotificationService>.Instance
        );
        MissingScopeRecordingHandler handler = new(
            notifications,
            new StubHttpContextAccessor(httpContext),
            NullLogger<MissingScopeRecordingHandler>.Instance
        );
        return (handler, db, chat);
    }

    private static async Task SeedTwitchConnectionAsync(AuthDbContext db)
    {
        // A connection that does NOT hold the missing scope, so the gap is real.
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = Tenant,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                Status = AuthEnums.IntegrationStatus.Connected,
                Scopes = ["moderator:read:followers"],
            }
        );
        await db.SaveChangesAsync();
    }

    private static TwitchHelixReauthRequiredEvent MissingScopeEvent() =>
        new()
        {
            BroadcasterId = Tenant,
            Provider = "twitch",
            ServiceName = "twitch",
            Reason = "missing_scope",
            MissingScope = MissingScope,
        };

    [Fact]
    public async Task WhenDetectedDuringADashboardRequest_RecordsTheGapButPostsNoChat()
    {
        (MissingScopeRecordingHandler handler, AuthDbContext db, SpyChatProvider chat) = Build(
            httpContext: new DefaultHttpContext()
        );
        await SeedTwitchConnectionAsync(db);

        await handler.HandleAsync(MissingScopeEvent());

        // The gap IS recorded — the dashboard banner + re-grant set still include it …
        ChannelMissingScope row = await db.ChannelMissingScopes.SingleAsync();
        row.Scope.Should().Be(MissingScope);
        // … but it is NEVER announced in the channel's public chat, and stays un-notified so a later autonomous
        // detection can still surface it.
        chat.Sent.Should().BeEmpty();
        row.ChatNotifiedAt.Should().BeNull();
    }

    [Fact]
    public async Task WhenDetectedOnTheAutonomousPath_RecordsTheGapAndAnnouncesOnceInChat()
    {
        (MissingScopeRecordingHandler handler, AuthDbContext db, SpyChatProvider chat) = Build(
            httpContext: null
        );
        await SeedTwitchConnectionAsync(db);

        await handler.HandleAsync(MissingScopeEvent());

        // No operator is at the dashboard, so the streamer is told once, in chat, which scope the bot needs.
        chat.Sent.Should().ContainSingle();
        chat.Sent[0].BroadcasterId.Should().Be(Tenant);
        chat.Sent[0].Message.Should().Contain(MissingScope);

        ChannelMissingScope row = await db.ChannelMissingScopes.SingleAsync();
        row.ChatNotifiedAt.Should().NotBeNull("the one-shot notice is stamped after it is sent");
    }

    /// <summary>A settable <see cref="IHttpContextAccessor"/> so a test can pose as a dashboard request or an autonomous job.</summary>
    private sealed class StubHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}
