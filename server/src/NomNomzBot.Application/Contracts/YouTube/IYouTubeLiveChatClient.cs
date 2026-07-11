// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.YouTube;

/// <summary>
/// Read-only transport over the YouTube Live Streaming API (Data API v3) for a streamer's OWN live chat —
/// the cross-platform chat READ seam for YouTube (combined-chat item 6). It resolves the caller's currently
/// active broadcast to its live-chat id, then pages that chat's messages. Pure HTTP I/O keyed by the
/// broadcaster's OAuth bearer (scope <c>youtube.readonly</c>); it holds no database or event-bus dependency,
/// mirroring the thin Twitch sub-clients — persisting messages and raising domain events is the poller's job.
/// Verified against the live API 2026-07-10: <c>liveBroadcasts.list</c> filters (<c>broadcastStatus</c> /
/// <c>mine</c> / <c>id</c>) are mutually exclusive, so the active broadcast is fetched with
/// <c>broadcastStatus=active</c> on the caller's token.
/// </summary>
public interface IYouTubeLiveChatClient
{
    /// <summary>
    /// Resolves the caller's currently active live broadcast to its live-chat id
    /// (<c>GET liveBroadcasts?part=snippet&amp;broadcastStatus=active</c>). A successful result with a
    /// <c>null</c> value means the caller is not live (no active broadcast) — a normal state, not an error;
    /// a failure is a transport/auth problem. <paramref name="accessToken"/> is the broadcaster's decrypted
    /// YouTube OAuth bearer.
    /// </summary>
    Task<Result<YouTubeActiveChat?>> GetActiveLiveChatAsync(
        string accessToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists one page of a live chat's messages (<c>GET liveChatMessages?part=snippet,authorDetails</c>).
    /// Pass <paramref name="pageToken"/> = the previous page's <see cref="YouTubeLiveChatPage.NextPageToken"/>
    /// to continue (null for the first read). The caller MUST wait
    /// <see cref="YouTubeLiveChatPage.PollingIntervalMs"/> before the next call (the API sets it per chat
    /// activity). Returns the messages published since the token, chronological.
    /// </summary>
    Task<Result<YouTubeLiveChatPage>> ListMessagesAsync(
        string accessToken,
        string liveChatId,
        string? pageToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Resolves the caller's own YouTube channel (<c>GET channels?part=snippet&amp;mine=true</c>) — the
    /// stable external id the poller provisions the platform <c>Channel</c> tenant under, plus the display
    /// title. A token whose Google account has no YouTube channel yields <c>NOT_FOUND</c>.
    /// </summary>
    Task<Result<YouTubeOwnChannel>> GetOwnChannelAsync(
        string accessToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a text message into a live chat (<c>POST liveChatMessages?part=snippet</c>, scope
    /// <c>youtube</c>/<c>youtube.force-ssl</c>) — the WRITE half of the seam (bot replies on YouTube,
    /// combined-chat item 6). YouTube caps a message at 200 characters; longer input is rejected
    /// <c>VALIDATION_FAILED</c> before any call. A token without the write scope maps to
    /// <c>MISSING_SCOPE</c>; a dead/ended chat id to <c>NOT_FOUND</c>.
    /// </summary>
    Task<Result> SendMessageAsync(
        string accessToken,
        string liveChatId,
        string text,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Bans a viewer from a live chat (<c>POST liveChat/bans?part=snippet</c>) — permanent when
    /// <paramref name="durationSeconds"/> is null, else a temporary ban (the Twitch-timeout analogue;
    /// YouTube clamps the duration server-side). <paramref name="bannedChannelId"/> is the viewer's
    /// YouTube channel id. Returns the created BAN resource id — the only key
    /// <c>liveChatBans.delete</c> (unban) accepts, so the caller must record it. Same failure mapping
    /// as the other write calls.
    /// </summary>
    Task<Result<string>> BanUserAsync(
        string accessToken,
        string liveChatId,
        string bannedChannelId,
        int? durationSeconds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lifts a ban (<c>DELETE liveChat/bans?id=</c>). <paramref name="banId"/> is the resource id the
    /// matching <see cref="BanUserAsync"/> returned. A <c>NOT_FOUND</c> means YouTube no longer has the
    /// ban (expired timeout, ended chat) — for the caller that outcome IS an unbanned user.
    /// </summary>
    Task<Result> UnbanUserAsync(
        string accessToken,
        string banId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes a single live-chat message (<c>DELETE liveChat/messages?id=</c>) — the message id is the
    /// one the read seam surfaced (and the dashboard's delete button sends back).
    /// </summary>
    Task<Result> DeleteMessageAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Retitles the caller's ACTIVE broadcast (channel-ops seam): resolves the active broadcast, then
    /// <c>PUT liveBroadcasts?part=snippet</c> with the new title — <c>snippet.scheduledStartTime</c> is
    /// carried over because the update REPLACES the snippet and the API requires it. Offline (no active
    /// broadcast) is <c>NOT_FOUND</c>: YouTube titles live on broadcasts, so there is nothing to retitle.
    /// Returns the applied title.
    /// </summary>
    Task<Result<string>> UpdateActiveBroadcastTitleAsync(
        string accessToken,
        string title,
        CancellationToken cancellationToken = default
    );
}

/// <summary>The authenticated user's own YouTube channel identity.</summary>
public sealed record YouTubeOwnChannel(string ChannelId, string Title);

/// <summary>The caller's active broadcast and its live-chat id.</summary>
public sealed record YouTubeActiveChat(string BroadcastId, string LiveChatId, string? Title);

/// <summary>One page of live-chat messages plus the paging cursor and the API-directed poll delay.</summary>
public sealed record YouTubeLiveChatPage(
    IReadOnlyList<YouTubeLiveChatMessage> Messages,
    string? NextPageToken,
    int PollingIntervalMs
);

/// <summary>
/// A single YouTube live-chat message, flattened to what the ingest needs: the author's YouTube channel id
/// and display name, the rendered text, when it was published, and the author's chat standing (owner /
/// moderator / member) so the domain event can carry the same role signal Twitch chat does.
/// </summary>
public sealed record YouTubeLiveChatMessage(
    string Id,
    string AuthorChannelId,
    string AuthorDisplayName,
    string DisplayText,
    DateTimeOffset PublishedAt,
    bool IsModerator,
    bool IsOwner,
    bool IsMember
);
