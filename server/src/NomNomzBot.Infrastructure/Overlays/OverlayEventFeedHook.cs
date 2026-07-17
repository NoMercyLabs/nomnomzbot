// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Overlays.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Overlays;

/// <summary>
/// The generic overlay event feed's source: one post-commit hook that fans EVERY journaled event out to the
/// channel's connected overlays via <see cref="IOverlayEventFeed"/> (widgets-overlays.md). Because it rides the
/// single "sees every event" seam, a custom overlay receives the whole event stream — chat, alerts, now-playing,
/// custom events — without one handler per type. Encrypted payloads and tenant-less events are skipped (an overlay
/// only ever gets its own channel's non-sensitive events, over the token-gated overlay group). A SHADOWBANNED
/// user's events (J.12) are also skipped — their activity never reaches bot-driven public surfaces (a merely
/// MUTED user's chat still shows; mute silences features, not visibility).
/// </summary>
public sealed class OverlayEventFeedHook(
    IOverlayEventFeed feed,
    IChannelRegistry registry,
    ILogger<OverlayEventFeedHook> logger
) : IJournalPostCommitHook
{
    public async Task<Result> OnCommittedAsync(
        EventRecord committed,
        CancellationToken cancellationToken = default
    )
    {
        if (committed.BroadcasterId is not Guid broadcasterId || broadcasterId == Guid.Empty)
            return Result.Success();

        // Never push an encrypted payload to a browser source — the overlay could not use it and it must not leave
        // the vault boundary in the clear.
        if (committed.PayloadIsEncrypted)
            return Result.Success();

        // Keep internal plumbing (EventSub lifecycle, token refresh, projections, raw wire topics) off the overlay
        // wire — overlays render user-facing facts only.
        if (!OverlayEventFilter.ShouldForward(committed.EventType))
            return Result.Success();

        if (IsFromShadowbannedUser(broadcasterId, committed.PayloadJson))
            return Result.Success();

        try
        {
            await feed.BroadcastEventAsync(
                broadcasterId,
                committed.EventType,
                committed.PayloadJson,
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort delivery: a hub push failure never rolls back the commit or blocks other hooks.
            logger.LogWarning(
                ex,
                "Overlay feed push failed for {EventType} ({EventId})",
                committed.EventType,
                committed.EventId
            );
        }

        return Result.Success();
    }

    /// <summary>
    /// True when the event's acting user is shadowbanned (or blacklisted — belt and braces; blacklisted
    /// chat never reaches the journal) on this channel. Fast path: no shadow/blacklist entries → no JSON
    /// parse. Attribution is read from the payload's <c>userId</c> (+ optional <c>provider</c>, Twitch
    /// implied for EventSub-born events); events with no user attribution always forward.
    /// </summary>
    private bool IsFromShadowbannedUser(Guid broadcasterId, string payloadJson)
    {
        ChannelContext? ctx = registry.Get(broadcasterId);
        if (ctx is null || ctx.ModerationStandings.IsEmpty)
            return false;
        bool anyHidden = ctx.ModerationStandings.Values.Any(s =>
            s is ModerationStanding.Shadowbanned or ModerationStanding.Blacklisted
        );
        if (!anyHidden)
            return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            string? userId =
                GetString(doc.RootElement, "userId") ?? GetString(doc.RootElement, "UserId");
            if (string.IsNullOrEmpty(userId))
                return false;
            string provider =
                GetString(doc.RootElement, "provider")
                ?? GetString(doc.RootElement, "Provider")
                ?? "twitch";

            string? standing = ctx.ModerationStandingFor(provider, userId);
            return standing is ModerationStanding.Shadowbanned or ModerationStanding.Blacklisted;
        }
        catch (JsonException)
        {
            return false; // unparseable payloads forward as before — never a new failure mode.
        }
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
