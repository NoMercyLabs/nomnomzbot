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
/// The top-level Helix façade (twitch-helix.md §3.1): a pure accessor exposing every category sub-client by
/// name for discoverability, so a consumer injects one client and reaches the whole Helix surface through
/// <c>IntelliSense</c> (<c>helix.Moderation.BanUserAsync(...)</c>) instead of memorising 26 interface names.
/// It holds no state and performs no I/O of its own; each sub-client it returns shares the same
/// <c>HttpClient("twitch-helix")</c>, rate limiter, identity resolver, and token resolver. Callers that
/// already know exactly one category may keep injecting that sub-client directly — the façade is additive.
/// </summary>
public interface ITwitchHelixClient
{
    ITwitchChannelsApi Channels { get; }
    ITwitchUsersApi Users { get; }
    ITwitchSearchApi Search { get; }
    ITwitchStreamsApi Streams { get; }
    ITwitchSubscriptionsApi Subscriptions { get; }
    ITwitchChannelPointsApi ChannelPoints { get; }
    ITwitchModerationApi Moderation { get; }
    ITwitchModeratorsApi Moderators { get; }
    ITwitchPollsApi Polls { get; }
    ITwitchPredictionsApi Predictions { get; }
    ITwitchRaidsApi Raids { get; }
    ITwitchChatApi Chat { get; }
    ITwitchChatAssetsApi ChatAssets { get; }
    ITwitchBitsApi Bits { get; }
    ITwitchClipsApi Clips { get; }
    ITwitchVideosApi Videos { get; }
    ITwitchScheduleApi Schedule { get; }
    ITwitchAdsApi Ads { get; }
    ITwitchCharityApi Charity { get; }
    ITwitchGoalsApi Goals { get; }
    ITwitchHypeTrainApi HypeTrain { get; }
    ITwitchTeamsApi Teams { get; }
    ITwitchGamesApi Games { get; }
    ITwitchContentClassificationApi ContentClassification { get; }
    ITwitchWhispersApi Whispers { get; }
    ITwitchGuestStarApi GuestStar { get; }
}
