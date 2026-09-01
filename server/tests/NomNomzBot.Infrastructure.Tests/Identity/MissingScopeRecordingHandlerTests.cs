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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
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
///
/// The autonomous-path announce goes through <see cref="IScopeNotificationDebouncer"/> (OWNER REQUEST
/// 2026-09-01, S-OWN02): several proactive jobs can each hit a DIFFERENT missing scope within the same
/// reconnect/onboarding pass, each via its own handler invocation — a burst of several handler calls must still
/// collapse into ONE chat message, not one per invocation.
/// </summary>
public sealed class MissingScopeRecordingHandlerTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private const string MissingScope = "moderator:read:shield_mode";
    private const string OtherMissingScope = "channel:read:vips";

    private static (
        MissingScopeRecordingHandler Handler,
        AuthDbContext Db,
        SpyChatProvider Chat,
        FakeTimeProvider TimeProvider,
        CapturingDebouncer Debouncer,
        string DatabaseName
    ) Build(HttpContext? httpContext)
    {
        string databaseName = Guid.NewGuid().ToString();
        AuthDbContext db = AuthTestBuilder.NewContext(databaseName);
        SpyChatProvider chat = new();
        FakeTimeProvider timeProvider = new();

        // The debouncer flushes on a FRESH scope (its own AuthDbContext) once its coalesce window elapses — a
        // second context over the SAME shared-cache SQLite database, exactly like a real DI scope reading the
        // same store the handler's own scope wrote to.
        ServiceCollection debounceServices = new();
        debounceServices.AddSingleton<IChatProvider>(chat);
        debounceServices.AddSingleton<IPlatformBotReadinessGate>(new StubBotReadiness(true));
        debounceServices.AddScoped<IApplicationDbContext>(_ =>
            AuthTestBuilder.NewContext(databaseName)
        );
        debounceServices.AddScoped<IScopeNotificationService>(sp => new ScopeNotificationService(
            sp.GetRequiredService<IApplicationDbContext>(),
            sp,
            new TwitchScopeRegistry(),
            timeProvider,
            NullLogger<ScopeNotificationService>.Instance
        ));
        ServiceProvider debounceProvider = debounceServices.BuildServiceProvider();
        CapturingDebouncer debouncer = new(
            new ScopeNotificationDebouncer(
                debounceProvider.GetRequiredService<IServiceScopeFactory>(),
                timeProvider,
                NullLogger<ScopeNotificationDebouncer>.Instance
            )
        );

        // The service resolves IChatProvider + IPlatformBotReadinessGate lazily, so the provider hands both back.
        IServiceProvider notificationProvider = new ServiceCollection()
            .AddSingleton<IChatProvider>(chat)
            .AddSingleton<IPlatformBotReadinessGate>(new StubBotReadiness(true))
            .BuildServiceProvider();
        ScopeNotificationService notifications = new(
            db,
            notificationProvider,
            new(),
            timeProvider,
            NullLogger<ScopeNotificationService>.Instance
        );
        MissingScopeRecordingHandler handler = new(
            notifications,
            debouncer,
            new StubHttpContextAccessor(httpContext),
            NullLogger<MissingScopeRecordingHandler>.Instance
        );
        return (handler, db, chat, timeProvider, debouncer, databaseName);
    }

    private static async Task SeedTwitchConnectionAsync(AuthDbContext db)
    {
        // A connection that does NOT hold the missing scope, so the gap is real.
        db.IntegrationConnections.Add(
            new()
            {
                BroadcasterId = Tenant,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                Status = AuthEnums.IntegrationStatus.Connected,
                Scopes = ["moderator:read:followers"],
            }
        );
        await db.SaveChangesAsync();
    }

    private static TwitchHelixReauthRequiredEvent MissingScopeEvent(string scope = MissingScope) =>
        new()
        {
            BroadcasterId = Tenant,
            Provider = "twitch",
            ServiceName = "twitch",
            Reason = "missing_scope",
            MissingScope = scope,
        };

    [Fact]
    public async Task WhenDetectedDuringADashboardRequest_RecordsTheGapButPostsNoChat()
    {
        (MissingScopeRecordingHandler handler, AuthDbContext db, SpyChatProvider chat, _, _, _) =
            Build(httpContext: new DefaultHttpContext());
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
    public async Task WhenDetectedOnTheAutonomousPath_RecordsTheGapAndAnnouncesOnceInChat_AfterTheCoalesceWindow()
    {
        (
            MissingScopeRecordingHandler handler,
            AuthDbContext db,
            SpyChatProvider chat,
            FakeTimeProvider timeProvider,
            CapturingDebouncer debouncer,
            string databaseName
        ) = Build(httpContext: null);
        await SeedTwitchConnectionAsync(db);

        await handler.HandleAsync(MissingScopeEvent());

        // Nothing posts yet — the flush is debounced until the coalesce window elapses.
        chat.Sent.Should().BeEmpty();

        timeProvider.Advance(ScopeNotificationDebouncer.CoalesceWindow);
        await debouncer.LastFlush!;

        // No operator is at the dashboard, so the streamer is told once, in chat, which scope the bot needs.
        chat.Sent.Should().ContainSingle();
        chat.Sent[0].BroadcasterId.Should().Be(Tenant);
        chat.Sent[0].Message.Should().Contain(MissingScope);

        // A FRESH context — the flush wrote through its own scoped DbContext, and `db`'s change tracker still
        // holds the pre-flush (un-notified) instance it loaded during RecordMissingScopeAsync.
        await using AuthDbContext verify = AuthTestBuilder.NewContext(databaseName);
        ChannelMissingScope row = await verify.ChannelMissingScopes.SingleAsync();
        row.ChatNotifiedAt.Should().NotBeNull("the one-shot notice is stamped after it is sent");
    }

    /// <summary>
    /// The regression this guards (OWNER REQUEST 2026-09-01, S-OWN02): several proactive jobs (community roster
    /// sync, subscriber/VIP standing, banned-user import) can each hit a DIFFERENT missing scope in the same
    /// reconnect/onboarding pass, each via its OWN handler invocation. Before the debouncer, each invocation's
    /// autonomous-path branch called <c>NotifyPendingAsync</c> directly — the first invocation's "batch of
    /// everything pending right now" always ran before the second job had recorded its own gap, so the two calls
    /// still produced two chat messages back to back even though each individual send was, in isolation, a
    /// correctly batched one-shot notice.
    /// </summary>
    [Fact]
    public async Task WhenTwoDifferentGapsAreDetectedBySeparateHandlerInvocationsInTheSameBurst_PostsOneBatchedMessage()
    {
        (
            MissingScopeRecordingHandler handler,
            AuthDbContext db,
            SpyChatProvider chat,
            FakeTimeProvider timeProvider,
            CapturingDebouncer debouncer,
            string databaseName
        ) = Build(httpContext: null);
        await SeedTwitchConnectionAsync(db);

        // Two SEPARATE handler invocations (as two sibling proactive jobs would each raise their own event),
        // a moment apart but both well inside the coalesce window.
        await handler.HandleAsync(MissingScopeEvent(MissingScope));
        timeProvider.Advance(TimeSpan.FromMilliseconds(200));
        await handler.HandleAsync(MissingScopeEvent(OtherMissingScope));

        chat.Sent.Should().BeEmpty("both gaps are still inside the coalesce window");

        timeProvider.Advance(ScopeNotificationDebouncer.CoalesceWindow);
        await debouncer.LastFlush!;

        // ONE message, covering both gaps — never one per handler invocation.
        chat.Sent.Should().ContainSingle();
        chat.Sent[0].Message.Should().Contain(MissingScope);
        chat.Sent[0].Message.Should().Contain(OtherMissingScope);

        // A FRESH context — see the note in the previous test about the original `db`'s stale change tracker.
        await using AuthDbContext verify = AuthTestBuilder.NewContext(databaseName);
        (await verify.ChannelMissingScopes.ToListAsync())
            .Should()
            .OnlyContain(row => row.ChatNotifiedAt != null);
    }

    /// <summary>A settable <see cref="IHttpContextAccessor"/> so a test can pose as a dashboard request or an autonomous job.</summary>
    private sealed class StubHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    /// <summary>Wraps a real debouncer and remembers the last scheduled flush so a test can await it deterministically.</summary>
    private sealed class CapturingDebouncer(IScopeNotificationDebouncer inner)
        : IScopeNotificationDebouncer
    {
        public Task? LastFlush { get; private set; }

        public Task RequestFlushAsync(Guid broadcasterId, CancellationToken ct = default)
        {
            Task flush = inner.RequestFlushAsync(broadcasterId, ct);
            LastFlush = flush;
            return flush;
        }
    }
}
