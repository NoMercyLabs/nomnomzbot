// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// The <see cref="ITwitchHelixClient"/> façade (twitch-helix.md §3.1): a thin, stateless accessor that
/// surfaces every category sub-client as a named property. It composes the scoped sub-clients DI already
/// builds — it constructs nothing and stores no state of its own, so it inherits each sub-client's shared
/// transport, rate limiter, and token/identity resolvers unchanged. Each property is a direct passthrough to
/// the matching constructor-injected sub-client.
/// </summary>
public sealed class TwitchHelixClient(
    ITwitchChannelsApi channels,
    ITwitchUsersApi users,
    ITwitchSearchApi search,
    ITwitchStreamsApi streams,
    ITwitchSubscriptionsApi subscriptions,
    ITwitchChannelPointsApi channelPoints,
    ITwitchModerationApi moderation,
    ITwitchModeratorsApi moderators,
    ITwitchPollsApi polls,
    ITwitchPredictionsApi predictions,
    ITwitchRaidsApi raids,
    ITwitchChatApi chat,
    ITwitchChatAssetsApi chatAssets,
    ITwitchBitsApi bits,
    ITwitchClipsApi clips,
    ITwitchVideosApi videos,
    ITwitchScheduleApi schedule,
    ITwitchAdsApi ads,
    ITwitchCharityApi charity,
    ITwitchGoalsApi goals,
    ITwitchHypeTrainApi hypeTrain,
    ITwitchTeamsApi teams,
    ITwitchGamesApi games,
    ITwitchContentClassificationApi contentClassification,
    ITwitchWhispersApi whispers,
    ITwitchGuestStarApi guestStar
) : ITwitchHelixClient
{
    public ITwitchChannelsApi Channels { get; } = channels;
    public ITwitchUsersApi Users { get; } = users;
    public ITwitchSearchApi Search { get; } = search;
    public ITwitchStreamsApi Streams { get; } = streams;
    public ITwitchSubscriptionsApi Subscriptions { get; } = subscriptions;
    public ITwitchChannelPointsApi ChannelPoints { get; } = channelPoints;
    public ITwitchModerationApi Moderation { get; } = moderation;
    public ITwitchModeratorsApi Moderators { get; } = moderators;
    public ITwitchPollsApi Polls { get; } = polls;
    public ITwitchPredictionsApi Predictions { get; } = predictions;
    public ITwitchRaidsApi Raids { get; } = raids;
    public ITwitchChatApi Chat { get; } = chat;
    public ITwitchChatAssetsApi ChatAssets { get; } = chatAssets;
    public ITwitchBitsApi Bits { get; } = bits;
    public ITwitchClipsApi Clips { get; } = clips;
    public ITwitchVideosApi Videos { get; } = videos;
    public ITwitchScheduleApi Schedule { get; } = schedule;
    public ITwitchAdsApi Ads { get; } = ads;
    public ITwitchCharityApi Charity { get; } = charity;
    public ITwitchGoalsApi Goals { get; } = goals;
    public ITwitchHypeTrainApi HypeTrain { get; } = hypeTrain;
    public ITwitchTeamsApi Teams { get; } = teams;
    public ITwitchGamesApi Games { get; } = games;
    public ITwitchContentClassificationApi ContentClassification { get; } = contentClassification;
    public ITwitchWhispersApi Whispers { get; } = whispers;
    public ITwitchGuestStarApi GuestStar { get; } = guestStar;
}
