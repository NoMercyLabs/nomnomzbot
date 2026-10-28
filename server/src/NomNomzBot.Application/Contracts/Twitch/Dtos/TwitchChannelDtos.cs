// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Channels" category wire models (GET/PATCH /channels, /channels/editors, /channels/followed,
// /channels/followers). These records deserialize straight from Twitch's snake_case JSON via the
// transport's naming policy — no per-property annotations. Twitch ids stay strings (they are other
// users' / channels' ids); the owning tenant is always passed in as a Guid method argument, never here.

/// <summary>Get Channel Information — the broadcaster's current title, category, language, tags and content labels.</summary>
public sealed record TwitchChannelInformation(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string BroadcasterLanguage,
    string GameId,
    string GameName,
    string Title,
    int Delay,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> ContentClassificationLabels,
    bool IsBrandedContent
);

/// <summary>One user with editor access to the channel (Get Channel Editors).</summary>
public sealed record TwitchChannelEditor(string UserId, string UserName, DateTimeOffset CreatedAt);

/// <summary>One follower of the channel (Get Channel Followers).</summary>
public sealed record TwitchChannelFollower(
    string UserId,
    string UserLogin,
    string UserName,
    DateTimeOffset FollowedAt
);

/// <summary>One channel the user follows (Get Followed Channels).</summary>
public sealed record TwitchFollowedChannel(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    DateTimeOffset FollowedAt
);

/// <summary>One content-classification-label toggle in a Modify Channel Information request body.</summary>
public sealed record TwitchContentClassificationLabelChoice(string Id, bool IsEnabled);

/// <summary>
/// Modify Channel Information request body. All fields optional — only the ones set are sent (the transport
/// omits nulls), matching Twitch's "patch only what you provide" semantics. The broadcaster is the Guid
/// method argument, not part of this body.
/// </summary>
public sealed record ModifyChannelInformationRequest(
    string? Title = null,
    string? GameId = null,
    string? BroadcasterLanguage = null,
    int? Delay = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<TwitchContentClassificationLabelChoice>? ContentClassificationLabels = null,
    bool? IsBrandedContent = null
);
