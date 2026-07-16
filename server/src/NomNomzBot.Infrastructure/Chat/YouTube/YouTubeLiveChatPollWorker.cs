// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// The YouTube chat READ ingest (combined-chat item 6) — polls each YouTube-connected streamer's live
/// chat and publishes every message as the canonical <see cref="ChatMessageReceivedEvent"/>
/// (<c>Provider = youtube</c>), so persistence, the dashboard hub push, and the analytics projections all
/// fire through the ONE substrate Twitch chat already uses.
///
/// Cadence is quota-aware (the Data API bills every call): while a channel is offline only a cheap
/// liveness probe runs every 2 minutes; while live, message pages follow the API-directed
/// <c>pollingIntervalMillis</c> with a 5-second floor. On going live the streamer's YouTube presence is
/// provisioned as its own tenant <c>Channel</c> row (<see cref="IPlatformChannelProvisioner"/>) keyed by
/// their YouTube channel id, and the FIRST page (which returns recent history, not new messages) only
/// bootstraps the paging cursor — everything after it flows live. A worker restart mid-stream re-reads
/// that history page the same way, so the feed never floods with duplicates.
///
/// The worker is also YouTube's live tracker: every live/offline transition stamps the tenant
/// <c>Channel.IsLive</c> row the dashboard's <c>platformsLive</c> aggregates, and the first
/// confirmed-offline probe after a (re)start sweeps a stale flag a mid-stream crash left behind.
/// </summary>
public sealed class YouTubeLiveChatPollWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LivenessInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MissingScopeBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TransientFailureBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LivePollFloor = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IYouTubeLiveChatClient _client;
    private readonly IYouTubeLiveChatSessionRegistry _sessions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YouTubeLiveChatPollWorker> _logger;

    // Per-broadcaster poll state, touched only from the single tick loop (and single-threaded tests).
    private readonly Dictionary<Guid, PollState> _states = [];

    public YouTubeLiveChatPollWorker(
        IServiceScopeFactory scopeFactory,
        IYouTubeLiveChatClient client,
        IYouTubeLiveChatSessionRegistry sessions,
        TimeProvider timeProvider,
        ILogger<YouTubeLiveChatPollWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _sessions = sessions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("YouTubeLiveChatPollWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
                await Task.Delay(TickInterval, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "YouTubeLiveChatPollWorker tick failed");
            }
        }
    }

    // Internal (not private) so tests can drive a single deterministic tick —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal async Task TickAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Every channel with an enabled YouTube connection is a poll candidate; a disconnect drops its
        // state so a stale live session never keeps polling a revoked token.
        List<Guid> connected = await db
            .Services.Where(s =>
                s.Name == "youtube" && s.Enabled && s.AccessToken != null && s.BroadcasterId != null
            )
            .Select(s => s.BroadcasterId!.Value)
            .ToListAsync(ct);

        foreach (Guid gone in _states.Keys.Where(id => !connected.Contains(id)).ToList())
        {
            if (_states.Remove(gone, out PollState? removed) && removed.LiveChatId is not null)
            {
                _sessions.SetOffline(removed.TenantId);
                // A disconnect ends our tracking — a tenant we can no longer observe must not keep
                // claiming to be live on the dashboard.
                await SetTenantLiveAsync(db, removed.TenantId, live: false, ct);
            }
        }

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (Guid broadcasterId in connected)
        {
            if (!_states.TryGetValue(broadcasterId, out PollState? state))
            {
                state = new PollState();
                _states[broadcasterId] = state;
            }

            if (now < state.NextDueUtc)
                continue;

            try
            {
                await PollChannelAsync(scope.ServiceProvider, broadcasterId, state, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "YouTube chat poll failed for broadcaster {BroadcasterId}",
                    broadcasterId
                );
                state.NextDueUtc = now + TransientFailureBackoff;
            }
        }
    }

    private async Task PollChannelAsync(
        IServiceProvider services,
        Guid broadcasterId,
        PollState state,
        CancellationToken ct
    )
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        IYouTubeAccessTokenProvider tokens =
            services.GetRequiredService<IYouTubeAccessTokenProvider>();
        string? accessToken = await tokens.GetAccessTokenAsync(broadcasterId, ct);
        if (accessToken is null)
        {
            // No usable token (vault miss / failed refresh) — the integration status flow owns re-auth.
            await GoOfflineAsync(services, state, now + MissingScopeBackoff, ct);
            return;
        }

        if (state.LiveChatId is null)
        {
            await ProbeLivenessAsync(services, broadcasterId, state, accessToken, now, ct);
            return;
        }

        await ReadPageAsync(services, state, accessToken, now, ct);
    }

    private async Task ProbeLivenessAsync(
        IServiceProvider services,
        Guid broadcasterId,
        PollState state,
        string accessToken,
        DateTime now,
        CancellationToken ct
    )
    {
        Result<YouTubeActiveChat?> active = await _client.GetActiveLiveChatAsync(accessToken, ct);
        if (active.IsFailure)
        {
            state.NextDueUtc =
                now
                + (
                    active.ErrorCode == "MISSING_SCOPE"
                        ? MissingScopeBackoff
                        : TransientFailureBackoff
                );
            if (active.ErrorCode == "MISSING_SCOPE")
                _logger.LogWarning(
                    "YouTube liveness probe for {BroadcasterId} lacks the required scope — re-grant needed",
                    broadcasterId
                );
            return;
        }

        if (active.Value is null)
        {
            // First confirmed-offline probe after a (re)start: a tenant row left IsLive=true by a crash
            // mid-stream would otherwise claim live forever — resolve the own-channel id ONCE and clear it.
            if (!state.StaleLiveChecked)
            {
                Result<YouTubeOwnChannel> identity = await _client.GetOwnChannelAsync(
                    accessToken,
                    ct
                );
                if (identity.IsSuccess)
                {
                    state.StaleLiveChecked = true;
                    IApplicationDbContext sweepDb =
                        services.GetRequiredService<IApplicationDbContext>();
                    Guid staleTenantId = await sweepDb
                        .Channels.Where(c =>
                            c.Provider == AuthEnums.Platform.YouTube
                            && c.ExternalChannelId == identity.Value.ChannelId
                        )
                        .Select(c => c.Id)
                        .FirstOrDefaultAsync(ct);
                    if (staleTenantId != Guid.Empty)
                        await SetTenantLiveAsync(sweepDb, staleTenantId, live: false, ct);
                }
            }

            state.NextDueUtc = now + LivenessInterval;
            return;
        }

        // Going live: pin the streamer's own YouTube channel identity and provision its tenant row —
        // the stable Guid every persisted message and hub push for this platform presence rides under.
        Result<YouTubeOwnChannel> own = await _client.GetOwnChannelAsync(accessToken, ct);
        if (own.IsFailure)
        {
            state.NextDueUtc = now + TransientFailureBackoff;
            return;
        }

        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        Channel? primary = await db.Channels.FirstOrDefaultAsync(c => c.Id == broadcasterId, ct);
        if (primary is null)
        {
            state.NextDueUtc = now + TransientFailureBackoff;
            return;
        }

        IPlatformChannelProvisioner provisioner =
            services.GetRequiredService<IPlatformChannelProvisioner>();
        Guid tenantId = await provisioner.GetOrCreateAsync(
            primary.OwnerUserId,
            AuthEnums.Platform.YouTube,
            own.Value.ChannelId,
            own.Value.Title,
            ct
        );

        state.GoLive(active.Value.LiveChatId, tenantId, own.Value.ChannelId);
        state.StaleLiveChecked = true; // live now — the crash-recovery sweep is moot for this state.
        state.NextDueUtc = now; // read the bootstrap page on the next due pass, immediately.
        // The send path (YouTubeChatPlatform) can now write into this chat on the primary channel's token.
        _sessions.SetLive(tenantId, broadcasterId, active.Value.LiveChatId);
        // The dashboard's platformsLive reads the tenant row — stamp it live alongside the session.
        await SetTenantLiveAsync(db, tenantId, live: true, ct);

        _logger.LogInformation(
            "YouTube live chat opened for {BroadcasterId} (tenant {TenantId}, chat {LiveChatId})",
            broadcasterId,
            tenantId,
            active.Value.LiveChatId
        );
    }

    private async Task ReadPageAsync(
        IServiceProvider services,
        PollState state,
        string accessToken,
        DateTime now,
        CancellationToken ct
    )
    {
        Result<YouTubeLiveChatPage> page = await _client.ListMessagesAsync(
            accessToken,
            state.LiveChatId!,
            state.PageToken,
            ct
        );

        if (page.IsFailure)
        {
            if (page.ErrorCode == "NOT_FOUND")
            {
                // The broadcast ended (or the chat id went stale) — back to cheap liveness probing.
                _logger.LogInformation(
                    "YouTube live chat closed for tenant {TenantId}",
                    state.TenantId
                );
                await GoOfflineAsync(services, state, now + LivenessInterval, ct);
                return;
            }

            await GoOfflineAsync(
                services,
                state,
                now
                    + (
                        page.ErrorCode == "MISSING_SCOPE"
                            ? MissingScopeBackoff
                            : TransientFailureBackoff
                    ),
                ct
            );
            return;
        }

        string? previousToken = state.PageToken;
        state.PageToken = page.Value.NextPageToken;
        state.NextDueUtc =
            now + Max(TimeSpan.FromMilliseconds(page.Value.PollingIntervalMs), LivePollFloor);

        // The FIRST page of a chat session returns recent history, not new messages — consume only the
        // paging cursor so a (re)start never floods the live feed or the journal with old lines.
        if (previousToken is null)
            return;

        if (page.Value.Messages.Count == 0)
            return;

        await PublishNewMessagesAsync(services, state, page.Value.Messages, ct);
    }

    private static async Task PublishNewMessagesAsync(
        IServiceProvider services,
        PollState state,
        IReadOnlyList<YouTubeLiveChatMessage> messages,
        CancellationToken ct
    )
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        IEventBus bus = services.GetRequiredService<IEventBus>();

        // Safety net against page overlap: anything already persisted has already been broadcast.
        List<string> pageIds = messages.Select(m => m.Id).ToList();
        List<string> existing = await db
            .ChatMessages.Where(m => pageIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(ct);
        HashSet<string> seen = [.. existing];

        foreach (YouTubeLiveChatMessage message in messages)
        {
            if (string.IsNullOrEmpty(message.Id) || !seen.Add(message.Id))
                continue;

            await bus.PublishAsync(
                new ChatMessageReceivedEvent
                {
                    BroadcasterId = state.TenantId,
                    Provider = AuthEnums.Platform.YouTube,
                    OccurredAt = message.PublishedAt,
                    MessageId = message.Id,
                    TwitchBroadcasterId = state.ExternalChannelId!,
                    UserId = message.AuthorChannelId,
                    UserDisplayName = message.AuthorDisplayName,
                    // YouTube has no login concept — the lowercased display name is the denormalized handle.
                    UserLogin = message.AuthorDisplayName.ToLowerInvariant(),
                    Message = message.DisplayText,
                    Fragments =
                    [
                        new ChatMessageFragment { Type = "text", Text = message.DisplayText },
                    ],
                    Badges = [],
                    IsSubscriber = message.IsMember,
                    IsVip = false,
                    IsModerator = message.IsModerator,
                    IsBroadcaster = message.IsOwner,
                },
                ct
            );
        }
    }

    /// <summary>
    /// Drops the poll state offline AND clears the send path's session AND un-stamps the tenant row's
    /// <c>IsLive</c> in the same stroke — the dashboard must never keep showing a platform we stopped
    /// tracking (chat ended, token lost) as live.
    /// </summary>
    private async Task GoOfflineAsync(
        IServiceProvider services,
        PollState state,
        DateTime nextDueUtc,
        CancellationToken ct
    )
    {
        if (state.LiveChatId is not null)
        {
            _sessions.SetOffline(state.TenantId);
            IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
            await SetTenantLiveAsync(db, state.TenantId, live: false, ct);
        }
        state.GoOffline(nextDueUtc);
    }

    /// <summary>Persists the platform tenant's live flag (no-op when already at the target state).</summary>
    private static async Task SetTenantLiveAsync(
        IApplicationDbContext db,
        Guid tenantId,
        bool live,
        CancellationToken ct
    )
    {
        Channel? tenant = await db.Channels.FirstOrDefaultAsync(c => c.Id == tenantId, ct);
        if (tenant is null || tenant.IsLive == live)
            return;
        tenant.IsLive = live;
        await db.SaveChangesAsync(ct);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    /// <summary>Mutable per-broadcaster cursor: offline (liveness probing) or live (page polling).</summary>
    private sealed class PollState
    {
        public DateTime NextDueUtc { get; set; } = DateTime.MinValue;
        public string? LiveChatId { get; private set; }
        public Guid TenantId { get; private set; }
        public string? ExternalChannelId { get; private set; }
        public string? PageToken { get; set; }

        /// <summary>Crash-recovery guard: true once a stale IsLive tenant row has been swept (or the
        /// channel went live, which supersedes the sweep).</summary>
        public bool StaleLiveChecked { get; set; }

        public void GoLive(string liveChatId, Guid tenantId, string externalChannelId)
        {
            LiveChatId = liveChatId;
            TenantId = tenantId;
            ExternalChannelId = externalChannelId;
            PageToken = null;
        }

        public void GoOffline(DateTime nextDueUtc)
        {
            LiveChatId = null;
            PageToken = null;
            NextDueUtc = nextDueUtc;
        }
    }
}
