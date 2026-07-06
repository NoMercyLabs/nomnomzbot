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

// Helix "Channel Points" category wire models (GET/POST/PATCH/DELETE /channel_points/custom_rewards and
// /channel_points/custom_rewards/redemptions). These records deserialize straight from Twitch's snake_case
// JSON via the transport's naming policy — no per-property annotations. Twitch ids stay strings; timestamps
// are DateTimeOffset; the owning tenant is always passed in as a Guid method argument, never modelled here.

/// <summary>The three CDN sizes of a custom-reward icon (the reward's own image and the platform default image).</summary>
public sealed record TwitchCustomRewardImage(string Url1x, string Url2x, string Url4x);

/// <summary>The per-stream redemption cap on a custom reward (off plus the cap when enabled).</summary>
public sealed record TwitchCustomRewardMaxPerStreamSetting(bool IsEnabled, int MaxPerStream);

/// <summary>The per-user-per-stream redemption cap on a custom reward.</summary>
public sealed record TwitchCustomRewardMaxPerUserPerStreamSetting(
    bool IsEnabled,
    int MaxPerUserPerStream
);

/// <summary>The global cooldown between redemptions of a custom reward (off plus the cooldown when enabled).</summary>
public sealed record TwitchCustomRewardGlobalCooldownSetting(
    bool IsEnabled,
    int GlobalCooldownSeconds
);

/// <summary>
/// A channel-points Custom Reward in full (Create / Get / Update Custom Reward responses) — the reward's
/// identity, redemption cost and prompt, enabled / paused / in-stock state, colours, icon images, and the
/// three cap/cooldown settings as their own nested records. <see cref="IsManageable"/> (Twitch's
/// <c>is_manageable</c>) is true only when THIS client_id created the reward — the precondition for
/// updating/deleting it or changing its redemption status through Helix.
/// </summary>
public sealed record TwitchCustomReward(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string Id,
    string Title,
    string Prompt,
    int Cost,
    TwitchCustomRewardImage? Image,
    TwitchCustomRewardImage DefaultImage,
    string BackgroundColor,
    bool IsEnabled,
    bool IsManageable,
    bool IsUserInputRequired,
    TwitchCustomRewardMaxPerStreamSetting MaxPerStreamSetting,
    TwitchCustomRewardMaxPerUserPerStreamSetting MaxPerUserPerStreamSetting,
    TwitchCustomRewardGlobalCooldownSetting GlobalCooldownSetting,
    bool IsPaused,
    bool IsInStock,
    bool ShouldRedemptionsSkipRequestQueue,
    int? RedemptionsRedeemedCurrentStream,
    DateTimeOffset? CooldownExpiresAt
);

/// <summary>The trimmed reward snapshot embedded in a redemption (only id / title / prompt / cost).</summary>
public sealed record TwitchRedemptionReward(string Id, string Title, string Prompt, int Cost);

/// <summary>
/// One redemption of a channel-points custom reward (Get Custom Reward Redemption / Update Redemption
/// Status responses) — who redeemed it, the reward snapshot, the viewer's text input, the lifecycle status,
/// and when it was redeemed.
/// </summary>
public sealed record TwitchCustomRewardRedemption(
    string BroadcasterId,
    string BroadcasterLogin,
    string BroadcasterName,
    string Id,
    string UserId,
    string UserName,
    string UserLogin,
    TwitchRedemptionReward Reward,
    string UserInput,
    string Status,
    DateTimeOffset RedeemedAt
);

/// <summary>
/// Create Custom Reward request body. <c>Title</c> and <c>Cost</c> are required; the rest are optional and
/// only the ones set are sent (the transport omits nulls). The broadcaster is the Guid method argument, not
/// part of this body.
/// </summary>
public sealed record CreateCustomRewardRequest(
    string Title,
    int Cost,
    string? Prompt = null,
    bool? IsEnabled = null,
    string? BackgroundColor = null,
    bool? IsUserInputRequired = null,
    bool? IsMaxPerStreamEnabled = null,
    int? MaxPerStream = null,
    bool? IsMaxPerUserPerStreamEnabled = null,
    int? MaxPerUserPerStream = null,
    bool? IsGlobalCooldownEnabled = null,
    int? GlobalCooldownSeconds = null,
    bool? ShouldRedemptionsSkipRequestQueue = null
);

/// <summary>
/// Update Custom Reward request body. All fields optional — only the ones set are sent (the transport omits
/// nulls), matching Twitch's "patch only what you provide" semantics. The broadcaster and the reward id are
/// method arguments, not part of this body.
/// </summary>
public sealed record UpdateCustomRewardRequest(
    string? Title = null,
    string? Prompt = null,
    int? Cost = null,
    string? BackgroundColor = null,
    bool? IsEnabled = null,
    bool? IsUserInputRequired = null,
    bool? IsMaxPerStreamEnabled = null,
    int? MaxPerStream = null,
    bool? IsMaxPerUserPerStreamEnabled = null,
    int? MaxPerUserPerStream = null,
    bool? IsGlobalCooldownEnabled = null,
    int? GlobalCooldownSeconds = null,
    bool? IsPaused = null,
    bool? ShouldRedemptionsSkipRequestQueue = null
);

/// <summary>Update Redemption Status request body — the only legal targets are <c>FULFILLED</c> and <c>CANCELED</c>.</summary>
public sealed record UpdateRedemptionStatusRequest(string Status);
