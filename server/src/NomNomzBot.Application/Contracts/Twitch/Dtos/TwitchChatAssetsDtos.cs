// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Chat assets" category wire models (GET /chat/chatters, /chat/emotes[/global|/set|/user],
// /chat/badges[/global], /chat/shared_chat_session). These records deserialize straight from Twitch's
// snake_case JSON via the transport's naming policy. Exception: the scale-suffixed image-url fields
// (image_url_1x/2x/4x, url_1x/2x/4x) carry an explicit [JsonPropertyName] — SnakeCaseLower renders
// "ImageUrl1x" as "image_url1x" (no underscore before the scale suffix), which misses Twitch's
// "image_url_1x" and deserializes the urls to null (badges/emotes would render without images). Twitch ids stay
// strings (they are other users' / channels' / emote-set / badge ids); timestamps are DateTimeOffset;
// the owning tenant is always passed in as a Guid method argument, never modelled here.
//
// Note: the emote-list responses carry a top-level "template" string (the CDN URL pattern shared by all
// emotes in the page). The generic data[] envelope used by the transport only surfaces the data[] rows,
// so that sibling "template" field is not exposed by these DTOs — emote image URLs are read from each
// emote's own "images" object instead.

/// <summary>One viewer currently connected to the broadcaster's chat session (Get Chatters).</summary>
public sealed record TwitchChatter(string UserId, string UserLogin, string UserName);

/// <summary>The three CDN sizes of an emote image (Get Channel/Global Emotes).</summary>
public sealed record TwitchEmoteImages(
    [property: JsonPropertyName("url_1x")] string Url1x,
    [property: JsonPropertyName("url_2x")] string Url2x,
    [property: JsonPropertyName("url_4x")] string Url4x
);

/// <summary>
/// One of the broadcaster's custom emotes (Get Channel Emotes) — its identity, rendered image sizes,
/// subscriber tier, emote type / set, and the available formats, scales and theme modes for building a
/// CDN URL from the response's shared template.
/// </summary>
public sealed record TwitchChannelEmote(
    string Id,
    string Name,
    TwitchEmoteImages Images,
    string Tier,
    string EmoteType,
    string EmoteSetId,
    IReadOnlyList<string> Format,
    IReadOnlyList<string> Scale,
    IReadOnlyList<string> ThemeMode
);

/// <summary>
/// One global emote available in every channel (Get Global Emotes) — identity, rendered image sizes, and
/// the available formats, scales and theme modes. Global emotes carry no tier / set / owner.
/// </summary>
public sealed record TwitchGlobalEmote(
    string Id,
    string Name,
    TwitchEmoteImages Images,
    IReadOnlyList<string> Format,
    IReadOnlyList<string> Scale,
    IReadOnlyList<string> ThemeMode
);

/// <summary>
/// One emote belonging to a requested emote set (Get Emote Sets) — identity, rendered image sizes, emote
/// type, set id, the owning channel's id, and the available formats, scales and theme modes.
/// </summary>
public sealed record TwitchEmoteSetEmote(
    string Id,
    string Name,
    TwitchEmoteImages Images,
    string EmoteType,
    string EmoteSetId,
    string OwnerId,
    IReadOnlyList<string> Format,
    IReadOnlyList<string> Scale,
    IReadOnlyList<string> ThemeMode
);

/// <summary>
/// One emote available to the user across all channels (Get User Emotes) — identity, emote type, set id,
/// the owning channel's id, and the available formats, scales and theme modes. This endpoint omits the
/// pre-rendered <c>images</c> object; URLs are built from the response template.
/// </summary>
public sealed record TwitchUserEmote(
    string Id,
    string Name,
    string EmoteType,
    string EmoteSetId,
    string OwnerId,
    IReadOnlyList<string> Format,
    IReadOnlyList<string> Scale,
    IReadOnlyList<string> ThemeMode
);

/// <summary>
/// One version (size / variant) of a chat badge (Get Channel/Global Chat Badges) — its id, the three CDN
/// image sizes, the display title and description, and the optional click action / URL.
/// </summary>
public sealed record TwitchChatBadgeVersion(
    string Id,
    [property: JsonPropertyName("image_url_1x")] string ImageUrl1x,
    [property: JsonPropertyName("image_url_2x")] string ImageUrl2x,
    [property: JsonPropertyName("image_url_4x")] string ImageUrl4x,
    string Title,
    string Description,
    string ClickAction,
    string ClickUrl
);

/// <summary>One chat-badge set (Get Channel/Global Chat Badges) — its set id and every version it offers.</summary>
public sealed record TwitchChatBadgeSet(
    string SetId,
    IReadOnlyList<TwitchChatBadgeVersion> Versions
);

/// <summary>One participant channel in a shared chat session (Get Shared Chat Session).</summary>
public sealed record TwitchSharedChatParticipant(string BroadcasterId);

/// <summary>
/// The active shared chat session for a channel (Get Shared Chat Session) — the session id, the host
/// channel, every participant channel, and when the session was created and last updated. Twitch returns
/// an empty <c>data[]</c> when the channel is not in a shared chat session.
/// </summary>
public sealed record TwitchSharedChatSession(
    string SessionId,
    string HostBroadcasterId,
    IReadOnlyList<TwitchSharedChatParticipant> Participants,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
