// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Bits" category wire models (GET /bits/leaderboard, /bits/cheermotes, /bits/custom_power_ups).
// These records deserialize straight from Twitch's snake_case JSON via the transport's naming policy — no
// per-property annotations. Twitch ids stay strings; timestamps are DateTimeOffset; the owning tenant is
// always passed in as a Guid method argument, never modelled here.

/// <summary>One row of the Bits leaderboard (Get Bits Leaderboard) — a user and their rank/score for the period.</summary>
public sealed record TwitchBitsLeaderboardEntry(
    string UserId,
    string UserLogin,
    string UserName,
    int Rank,
    int Score
);

/// <summary>
/// The set of cheermote images for one theme/format combination, keyed by scale. The scale keys Twitch sends
/// (<c>1</c>, <c>1.5</c>, <c>2</c>, <c>3</c>, <c>4</c>) are not valid C# identifiers and the DTO rule forbids
/// <c>[JsonPropertyName]</c>, so this level is captured as a scale-string → URL <see cref="IReadOnlyDictionary{TKey,TValue}"/>
/// rather than a fixed-property record. This faithfully preserves every scale, including the awkward <c>1.5</c>.
/// </summary>
public sealed record TwitchCheermoteImageScales(IReadOnlyDictionary<string, string> Scales);

/// <summary>One cheermote-image format pair (animated vs static) for a theme, each a scale → URL map.</summary>
public sealed record TwitchCheermoteImageFormats(
    TwitchCheermoteImageScales Animated,
    TwitchCheermoteImageScales Static
);

/// <summary>The full cheermote image set for one tier — the light and dark themes, each holding the animated/static formats.</summary>
public sealed record TwitchCheermoteImages(
    TwitchCheermoteImageFormats Light,
    TwitchCheermoteImageFormats Dark
);

/// <summary>
/// One tier of a cheermote (Get Cheermotes) — the minimum bits it represents, the tier id, its colour, the
/// full image set per theme/format/scale, and whether it can be cheered / shown in the Bits card.
/// </summary>
public sealed record TwitchCheermoteTier(
    int MinBits,
    string Id,
    string Color,
    TwitchCheermoteImages Images,
    bool CanCheer,
    bool ShowInBitsCard
);

/// <summary>
/// One cheermote (Get Cheermotes) — its chat prefix, the ordered tiers, the cheermote type, its display
/// order, when it last changed, and whether it is charitable.
/// </summary>
public sealed record TwitchCheermote(
    string Prefix,
    IReadOnlyList<TwitchCheermoteTier> Tiers,
    string Type,
    int Order,
    DateTimeOffset LastUpdated,
    bool IsCharitable
);

/// <summary>The three CDN sizes of a custom power-up image (the power-up's own image and the platform default image).</summary>
public sealed record TwitchCustomPowerUpImage(string Url1x, string Url2x, string Url4x);

/// <summary>The per-stream redemption cap on a custom power-up (off plus the cap when enabled).</summary>
public sealed record TwitchCustomPowerUpMaxPerStreamSetting(bool IsEnabled, int MaxPerStream);

/// <summary>The per-user-per-stream redemption cap on a custom power-up.</summary>
public sealed record TwitchCustomPowerUpMaxPerUserPerStreamSetting(
    bool IsEnabled,
    int MaxPerUserPerStream
);

/// <summary>The global cooldown between redemptions of a custom power-up (off plus the cooldown when enabled).</summary>
public sealed record TwitchCustomPowerUpGlobalCooldownSetting(
    bool IsEnabled,
    int GlobalCooldownSeconds
);

/// <summary>
/// A Bits custom power-up in full (Get Custom Power-up response) — the power-up's identity, its bits cost and
/// prompt, enabled / paused / in-stock state, colours, icon images, the three cap/cooldown settings as their
/// own nested records, and the current-stream redemption tally / cooldown expiry when present.
/// </summary>
public sealed record TwitchCustomPowerUp(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string Id,
    string Title,
    string Prompt,
    int Bits,
    TwitchCustomPowerUpImage? Image,
    TwitchCustomPowerUpImage DefaultImage,
    string BackgroundColor,
    bool IsEnabled,
    bool IsUserInputRequired,
    bool IsPaused,
    bool IsInStock,
    TwitchCustomPowerUpMaxPerStreamSetting MaxPerStreamSetting,
    TwitchCustomPowerUpMaxPerUserPerStreamSetting MaxPerUserPerStreamSetting,
    TwitchCustomPowerUpGlobalCooldownSetting GlobalCooldownSetting,
    int? RedemptionsRedeemedCurrentStream,
    DateTimeOffset? CooldownExpiresAt
);
