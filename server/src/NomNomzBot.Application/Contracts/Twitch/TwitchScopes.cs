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

/// <summary>
/// The single source of truth for Twitch OAuth scope strings (twitch-helix.md §9.1). Every Helix
/// sub-client references these constants for its per-method scope pre-check (<see cref="ITwitchTokenResolver.HasScopeAsync"/>)
/// instead of inlining the raw string, so the scope catalogue and the progressive-grant logic share one list.
/// Grouped by the API category that consumes them; the exact required scope per endpoint is documented on
/// the Twitch API reference and asserted by each sub-client.
/// </summary>
public static class TwitchScopes
{
    // ── Ads ──
    public const string ChannelReadAds = "channel:read:ads";
    public const string ChannelManageAds = "channel:manage:ads";
    public const string ChannelEditCommercial = "channel:edit:commercial";

    // ── Bits ──
    public const string BitsRead = "bits:read";

    // ── Channels / broadcast ──
    public const string ChannelReadEditors = "channel:read:editors";
    public const string ChannelManageBroadcast = "channel:manage:broadcast";
    public const string UserReadBroadcast = "user:read:broadcast";
    public const string UserEditBroadcast = "user:edit:broadcast";
    public const string UserReadFollows = "user:read:follows";
    public const string ModeratorReadFollowers = "moderator:read:followers";

    // ── Channel points ──
    public const string ChannelReadRedemptions = "channel:read:redemptions";
    public const string ChannelManageRedemptions = "channel:manage:redemptions";

    // ── Charity ──
    public const string ChannelReadCharity = "channel:read:charity";

    // ── Chat ──
    public const string ModeratorReadChatters = "moderator:read:chatters";
    public const string UserReadEmotes = "user:read:emotes";
    public const string ModeratorManageChatSettings = "moderator:manage:chat_settings";
    public const string ModeratorManageAnnouncements = "moderator:manage:announcements";
    public const string ModeratorManageShoutouts = "moderator:manage:shoutouts";
    public const string ModeratorManageChatMessages = "moderator:manage:chat_messages";
    public const string UserWriteChat = "user:write:chat";
    public const string UserBot = "user:bot";
    public const string ChannelBot = "channel:bot";
    public const string UserManageChatColor = "user:manage:chat_color";

    // ── Clips ──
    public const string ClipsEdit = "clips:edit";
    public const string ChannelManageClips = "channel:manage:clips";
    public const string EditorManageClips = "editor:manage:clips";

    // ── Goals / Hype Train ──
    public const string ChannelReadGoals = "channel:read:goals";
    public const string ChannelReadHypeTrain = "channel:read:hype_train";

    // ── Moderation ──
    public const string ModerationRead = "moderation:read";
    public const string ModeratorManageBannedUsers = "moderator:manage:banned_users";
    public const string ModeratorReadBlockedTerms = "moderator:read:blocked_terms";
    public const string ModeratorManageBlockedTerms = "moderator:manage:blocked_terms";
    public const string ModeratorReadAutoModSettings = "moderator:read:automod_settings";
    public const string ModeratorManageAutoModSettings = "moderator:manage:automod_settings";
    public const string ModeratorManageAutoMod = "moderator:manage:automod";
    public const string ChannelManageModerators = "channel:manage:moderators";
    public const string ChannelReadVips = "channel:read:vips";
    public const string ChannelManageVips = "channel:manage:vips";
    public const string ModeratorReadShieldMode = "moderator:read:shield_mode";
    public const string ModeratorManageShieldMode = "moderator:manage:shield_mode";
    public const string ModeratorReadUnbanRequests = "moderator:read:unban_requests";
    public const string ModeratorManageUnbanRequests = "moderator:manage:unban_requests";
    public const string ModeratorManageWarnings = "moderator:manage:warnings";
    public const string ModeratorManageSuspiciousUsers = "moderator:manage:suspicious_users";
    public const string UserReadModeratedChannels = "user:read:moderated_channels";

    // ── Polls / Predictions / Raids / Schedule ──
    public const string ChannelReadPolls = "channel:read:polls";
    public const string ChannelManagePolls = "channel:manage:polls";
    public const string ChannelReadPredictions = "channel:read:predictions";
    public const string ChannelManagePredictions = "channel:manage:predictions";
    public const string ChannelManageRaids = "channel:manage:raids";
    public const string ChannelManageSchedule = "channel:manage:schedule";

    // ── Streams ──
    public const string ChannelReadStreamKey = "channel:read:stream_key";

    // ── Subscriptions ──
    public const string ChannelReadSubscriptions = "channel:read:subscriptions";
    public const string UserReadSubscriptions = "user:read:subscriptions";

    // ── Users ──
    public const string UserReadBlockedUsers = "user:read:blocked_users";
    public const string UserManageBlockedUsers = "user:manage:blocked_users";
    public const string UserEdit = "user:edit";
    public const string UserManageWhispers = "user:manage:whispers";

    // ── Videos ──
    public const string ChannelManageVideos = "channel:manage:videos";
}
