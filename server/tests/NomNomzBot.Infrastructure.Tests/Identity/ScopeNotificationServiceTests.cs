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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the general missing-scope mechanism (identity-auth §3.4a): the missing-scope set is computed from
/// granted-vs-required (the offered-feature → scope registry), a runtime <c>missing_scope</c> failure records a
/// gap exactly once, the chat notice fires exactly once per gap (idempotent), a re-grant clears the gap, and the
/// re-grant scope set is the union (granted ∪ missing) so the existing grant is never dropped.
/// </summary>
public sealed class ScopeNotificationServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");

    private static (
        ScopeNotificationService Service,
        AuthDbContext Db,
        SpyChatProvider Chat,
        StubBotReadiness Bot
    ) Build(bool botReady = true)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        SpyChatProvider chat = new();
        StubBotReadiness bot = new(botReady);
        // The service resolves IChatProvider + IPlatformBotReadinessGate lazily (to break the DI cycle), so the
        // harness supplies a minimal provider that hands both back — proving the real lazy-resolution path, not a
        // constructor injection.
        IServiceProvider provider = new ServiceCollection()
            .AddSingleton<IChatProvider>(chat)
            .AddSingleton<IPlatformBotReadinessGate>(bot)
            .BuildServiceProvider();
        ScopeNotificationService service = new(
            db,
            provider,
            TimeProvider.System,
            NullLogger<ScopeNotificationService>.Instance
        );
        return (service, db, chat, bot);
    }

    private static async Task SeedTwitchConnectionAsync(AuthDbContext db, params string[] scopes)
    {
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = Tenant,
                Provider = AuthEnums.IntegrationProvider.Twitch,
                Status = AuthEnums.IntegrationStatus.Connected,
                Scopes = [.. scopes],
            }
        );
        await db.SaveChangesAsync();
    }

    // ─── granted-vs-required computation ────────────────────────────────────────

    [Fact]
    public async Task GetMissingScopes_ReportsEveryOfferedScopeTheConnectionLacks_GroupedByFeature()
    {
        (ScopeNotificationService service, AuthDbContext db, _, _) = Build();
        // Holds the followers scope only — every other offered feature's scope is missing.
        await SeedTwitchConnectionAsync(db, "moderator:read:followers");

        Result<MissingScopesDto> result = await service.GetMissingScopesAsync(Tenant);

        result.IsSuccess.Should().BeTrue();
        IReadOnlyList<string> missing = [.. result.Value.Scopes.Select(s => s.Scope)];

        // The granted scope is NOT listed; representative missing ones ARE.
        missing.Should().NotContain("moderator:read:followers");
        missing.Should().Contain("channel:read:subscriptions");
        missing.Should().Contain("channel:read:polls");

        // A scope that gates a feature carries that feature attribution.
        MissingScopeDto subs = result.Value.Scopes.Single(s =>
            s.Scope == "channel:read:subscriptions"
        );
        subs.Features.Should().Contain("subscriptions");
        // Purely proactive (the connection never held it) — not detected from a runtime failure.
        subs.DetectedAtRuntime.Should().BeFalse();
        subs.ChatNotified.Should().BeFalse();
    }

    [Fact]
    public async Task GetMissingScopes_WhenAllOfferedScopesGranted_IsEmpty()
    {
        (ScopeNotificationService service, AuthDbContext db, _, _) = Build();
        // Every scope every offered feature needs, in one grant.
        string[] all =
        [
            "channel:read:subscriptions",
            "bits:read",
            "channel:read:redemptions",
            "channel:manage:redemptions",
            "channel:manage:raids",
            "channel:manage:broadcast",
            "channel:read:polls",
            "channel:manage:polls",
            "channel:read:predictions",
            "channel:manage:predictions",
            "moderator:read:followers",
            "moderator:manage:banned_users",
            "moderator:manage:chat_messages",
            "moderator:manage:automod",
            "channel:read:vips",
            "channel:manage:vips",
            "user:read:chat",
            "user:write:chat",
            "user:read:emotes",
        ];
        await SeedTwitchConnectionAsync(db, all);

        Result<MissingScopesDto> result = await service.GetMissingScopesAsync(Tenant);

        result.Value.Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMissingScopes_WhenNoTwitchConnection_IsNotFound()
    {
        (ScopeNotificationService service, _, _, _) = Build();

        Result<MissingScopesDto> result = await service.GetMissingScopesAsync(Tenant);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ─── reactive detection + idempotency ───────────────────────────────────────

    [Fact]
    public async Task RecordMissingScope_FirstTimeRecordsAGap_ThenIsIdempotent()
    {
        (ScopeNotificationService service, AuthDbContext db, _, _) = Build();
        await SeedTwitchConnectionAsync(db, "moderator:read:followers");

        Result<bool> first = await service.RecordMissingScopeAsync(
            Tenant,
            "channel:read:subscriptions",
            "subscriptions"
        );
        Result<bool> second = await service.RecordMissingScopeAsync(
            Tenant,
            "channel:read:subscriptions",
            "subscriptions"
        );

        first.Value.Should().BeTrue("the first detection records a new gap");
        second.Value.Should().BeFalse("a re-detection of the same gap is a no-op");

        // Exactly one row — never duplicated.
        List<ChannelMissingScope> rows = await db
            .ChannelMissingScopes.Where(m => m.BroadcasterId == Tenant)
            .ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Scope.Should().Be("channel:read:subscriptions");
        rows[0].Feature.Should().Be("subscriptions");
        rows[0].ChatNotifiedAt.Should().BeNull();
    }

    [Fact]
    public async Task RecordMissingScope_ForAScopeTheConnectionActuallyHolds_IsANoOp()
    {
        (ScopeNotificationService service, AuthDbContext db, _, _) = Build();
        await SeedTwitchConnectionAsync(db, "channel:read:subscriptions");

        // A stale failure for a scope that is in fact granted must not get stuck as a gap.
        Result<bool> recorded = await service.RecordMissingScopeAsync(
            Tenant,
            "channel:read:subscriptions",
            "subscriptions"
        );

        recorded.Value.Should().BeFalse();
        (await db.ChannelMissingScopes.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task NotifyPending_PostsExactlyOneChatMessagePerGap_AndNeverRepeats()
    {
        (ScopeNotificationService service, AuthDbContext db, SpyChatProvider chat, _) = Build();
        await SeedTwitchConnectionAsync(db, "moderator:read:followers");
        await service.RecordMissingScopeAsync(
            Tenant,
            "channel:read:subscriptions",
            "subscriptions"
        );

        Result<int> firstRun = await service.NotifyPendingAsync(Tenant);
        Result<int> secondRun = await service.NotifyPendingAsync(Tenant);

        firstRun.Value.Should().Be(1, "the gap is announced once");
        secondRun.Value.Should().Be(0, "an already-announced gap is never re-announced");

        // Exactly one message, to the right channel, naming the scope.
        chat.Sent.Should().ContainSingle();
        chat.Sent[0].BroadcasterId.Should().Be(Tenant);
        chat.Sent[0].Message.Should().Contain("channel:read:subscriptions");

        // The row is stamped notified — the idempotency anchor.
        ChannelMissingScope row = await db.ChannelMissingScopes.SingleAsync();
        row.ChatNotifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task NotifyPending_WhenBotCannotPost_DefersAndLeavesTheGapUnnotified()
    {
        (ScopeNotificationService service, AuthDbContext db, SpyChatProvider chat, _) = Build(
            botReady: false
        );
        await SeedTwitchConnectionAsync(db, "moderator:read:followers");
        await service.RecordMissingScopeAsync(
            Tenant,
            "channel:read:subscriptions",
            "subscriptions"
        );

        Result<int> run = await service.NotifyPendingAsync(Tenant);

        run.Value.Should().Be(0, "the one-shot notice is not burned on a send that would fail");
        chat.Sent.Should().BeEmpty();
        ChannelMissingScope row = await db.ChannelMissingScopes.SingleAsync();
        row.ChatNotifiedAt.Should().BeNull("the gap stays pending for a later retry");
    }

    // ─── re-grant clears the gap + union scope set ──────────────────────────────

    [Fact]
    public async Task ClearResolved_RemovesGapsTheNewGrantSatisfies()
    {
        (ScopeNotificationService service, AuthDbContext db, _, _) = Build();
        await SeedTwitchConnectionAsync(db, "moderator:read:followers");
        await service.RecordMissingScopeAsync(
            Tenant,
            "channel:read:subscriptions",
            "subscriptions"
        );
        await service.RecordMissingScopeAsync(Tenant, "bits:read", "bits");

        // A re-grant now holds the subscriptions scope (but not bits) — only that gap clears.
        Result<IReadOnlyList<string>> cleared = await service.ClearResolvedAsync(
            Tenant,
            ["moderator:read:followers", "channel:read:subscriptions"]
        );

        cleared.Value.Should().ContainSingle().Which.Should().Be("channel:read:subscriptions");
        List<string> remaining =
        [
            .. (await db.ChannelMissingScopes.ToListAsync()).Select(m => m.Scope),
        ];
        remaining.Should().ContainSingle().Which.Should().Be("bits:read");
    }

    [Fact]
    public async Task BuildRegrantScopeSet_IsTheUnionOfGrantedAndMissing_NeverDroppingExisting()
    {
        (ScopeNotificationService service, AuthDbContext db, _, _) = Build();
        // Already holds two scopes; everything else an offered feature needs is missing.
        await SeedTwitchConnectionAsync(db, "moderator:read:followers", "bits:read");

        Result<IReadOnlyList<string>> regrant = await service.BuildRegrantScopeSetAsync(Tenant);

        regrant.IsSuccess.Should().BeTrue();
        // The union KEEPS the already-granted scopes …
        regrant.Value.Should().Contain("moderator:read:followers");
        regrant.Value.Should().Contain("bits:read");
        // … AND adds the missing ones.
        regrant.Value.Should().Contain("channel:read:subscriptions");
        regrant.Value.Should().Contain("channel:manage:polls");
        // No duplicates.
        regrant.Value.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task BuildRegrantScopeSet_WhenNothingMissing_ReportsNoMissingScopes()
    {
        (ScopeNotificationService service, AuthDbContext db, _, _) = Build();
        string[] all =
        [
            "channel:read:subscriptions",
            "bits:read",
            "channel:read:redemptions",
            "channel:manage:redemptions",
            "channel:manage:raids",
            "channel:manage:broadcast",
            "channel:read:polls",
            "channel:manage:polls",
            "channel:read:predictions",
            "channel:manage:predictions",
            "moderator:read:followers",
            "moderator:manage:banned_users",
            "moderator:manage:chat_messages",
            "moderator:manage:automod",
            "channel:read:vips",
            "channel:manage:vips",
            "user:read:chat",
            "user:write:chat",
            "user:read:emotes",
        ];
        await SeedTwitchConnectionAsync(db, all);

        Result<IReadOnlyList<string>> regrant = await service.BuildRegrantScopeSetAsync(Tenant);

        regrant.IsFailure.Should().BeTrue();
        regrant.ErrorCode.Should().Be("NO_MISSING_SCOPES");
    }
}

/// <summary>Records every chat message the service sends so a test can assert the side effect + its idempotency.</summary>
internal sealed class SpyChatProvider : IChatProvider
{
    public List<(Guid BroadcasterId, string Message)> Sent { get; } = [];

    public Task<bool> SendMessageAsync(
        Guid broadcasterId,
        string message,
        CancellationToken cancellationToken = default
    )
    {
        Sent.Add((broadcasterId, message));
        return Task.FromResult(true);
    }

    public Task SendReplyAsync(
        Guid broadcasterId,
        string replyToMessageId,
        string message,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task TimeoutUserAsync(
        Guid broadcasterId,
        string userId,
        int durationSeconds,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task BanUserAsync(
        Guid broadcasterId,
        string userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task UnbanUserAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task DeleteMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;
}

/// <summary>A bot-readiness stub so the notice path can be exercised with the bot reachable or not.</summary>
internal sealed class StubBotReadiness(bool ready) : IPlatformBotReadinessGate
{
    public Task<bool> IsPlatformBotConfiguredAsync(CancellationToken ct = default) =>
        Task.FromResult(ready);
}
