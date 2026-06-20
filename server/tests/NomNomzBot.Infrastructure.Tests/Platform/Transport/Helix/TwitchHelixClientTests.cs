// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// The façade is a pure accessor over 26 near-identical sub-client properties, so its one real failure mode
/// is a copy-paste mis-wire (a property returning the wrong field). These tests pin every accessor to the
/// exact instance it was constructed with, so a mismap fails for the right reason.
/// </summary>
public class TwitchHelixClientTests
{
    private readonly ITwitchChannelsApi _channels = Substitute.For<ITwitchChannelsApi>();
    private readonly ITwitchUsersApi _users = Substitute.For<ITwitchUsersApi>();
    private readonly ITwitchSearchApi _search = Substitute.For<ITwitchSearchApi>();
    private readonly ITwitchStreamsApi _streams = Substitute.For<ITwitchStreamsApi>();
    private readonly ITwitchSubscriptionsApi _subscriptions =
        Substitute.For<ITwitchSubscriptionsApi>();
    private readonly ITwitchChannelPointsApi _channelPoints =
        Substitute.For<ITwitchChannelPointsApi>();
    private readonly ITwitchModerationApi _moderation = Substitute.For<ITwitchModerationApi>();
    private readonly ITwitchModeratorsApi _moderators = Substitute.For<ITwitchModeratorsApi>();
    private readonly ITwitchPollsApi _polls = Substitute.For<ITwitchPollsApi>();
    private readonly ITwitchPredictionsApi _predictions = Substitute.For<ITwitchPredictionsApi>();
    private readonly ITwitchRaidsApi _raids = Substitute.For<ITwitchRaidsApi>();
    private readonly ITwitchChatApi _chat = Substitute.For<ITwitchChatApi>();
    private readonly ITwitchChatAssetsApi _chatAssets = Substitute.For<ITwitchChatAssetsApi>();
    private readonly ITwitchBitsApi _bits = Substitute.For<ITwitchBitsApi>();
    private readonly ITwitchClipsApi _clips = Substitute.For<ITwitchClipsApi>();
    private readonly ITwitchVideosApi _videos = Substitute.For<ITwitchVideosApi>();
    private readonly ITwitchScheduleApi _schedule = Substitute.For<ITwitchScheduleApi>();
    private readonly ITwitchAdsApi _ads = Substitute.For<ITwitchAdsApi>();
    private readonly ITwitchCharityApi _charity = Substitute.For<ITwitchCharityApi>();
    private readonly ITwitchGoalsApi _goals = Substitute.For<ITwitchGoalsApi>();
    private readonly ITwitchHypeTrainApi _hypeTrain = Substitute.For<ITwitchHypeTrainApi>();
    private readonly ITwitchTeamsApi _teams = Substitute.For<ITwitchTeamsApi>();
    private readonly ITwitchGamesApi _games = Substitute.For<ITwitchGamesApi>();
    private readonly ITwitchContentClassificationApi _contentClassification =
        Substitute.For<ITwitchContentClassificationApi>();
    private readonly ITwitchWhispersApi _whispers = Substitute.For<ITwitchWhispersApi>();
    private readonly ITwitchGuestStarApi _guestStar = Substitute.For<ITwitchGuestStarApi>();

    private TwitchHelixClient CreateSut() =>
        new(
            _channels,
            _users,
            _search,
            _streams,
            _subscriptions,
            _channelPoints,
            _moderation,
            _moderators,
            _polls,
            _predictions,
            _raids,
            _chat,
            _chatAssets,
            _bits,
            _clips,
            _videos,
            _schedule,
            _ads,
            _charity,
            _goals,
            _hypeTrain,
            _teams,
            _games,
            _contentClassification,
            _whispers,
            _guestStar
        );

    [Fact]
    public void Every_accessor_returns_the_sub_client_it_was_constructed_with()
    {
        TwitchHelixClient sut = CreateSut();

        sut.Channels.Should().BeSameAs(_channels);
        sut.Users.Should().BeSameAs(_users);
        sut.Search.Should().BeSameAs(_search);
        sut.Streams.Should().BeSameAs(_streams);
        sut.Subscriptions.Should().BeSameAs(_subscriptions);
        sut.ChannelPoints.Should().BeSameAs(_channelPoints);
        sut.Moderation.Should().BeSameAs(_moderation);
        sut.Moderators.Should().BeSameAs(_moderators);
        sut.Polls.Should().BeSameAs(_polls);
        sut.Predictions.Should().BeSameAs(_predictions);
        sut.Raids.Should().BeSameAs(_raids);
        sut.Chat.Should().BeSameAs(_chat);
        sut.ChatAssets.Should().BeSameAs(_chatAssets);
        sut.Bits.Should().BeSameAs(_bits);
        sut.Clips.Should().BeSameAs(_clips);
        sut.Videos.Should().BeSameAs(_videos);
        sut.Schedule.Should().BeSameAs(_schedule);
        sut.Ads.Should().BeSameAs(_ads);
        sut.Charity.Should().BeSameAs(_charity);
        sut.Goals.Should().BeSameAs(_goals);
        sut.HypeTrain.Should().BeSameAs(_hypeTrain);
        sut.Teams.Should().BeSameAs(_teams);
        sut.Games.Should().BeSameAs(_games);
        sut.ContentClassification.Should().BeSameAs(_contentClassification);
        sut.Whispers.Should().BeSameAs(_whispers);
        sut.GuestStar.Should().BeSameAs(_guestStar);
    }

    [Fact]
    public void Accessors_expose_distinct_sub_clients_with_no_shared_mapping()
    {
        TwitchHelixClient sut = CreateSut();

        object[] exposed =
        [
            sut.Channels,
            sut.Users,
            sut.Search,
            sut.Streams,
            sut.Subscriptions,
            sut.ChannelPoints,
            sut.Moderation,
            sut.Moderators,
            sut.Polls,
            sut.Predictions,
            sut.Raids,
            sut.Chat,
            sut.ChatAssets,
            sut.Bits,
            sut.Clips,
            sut.Videos,
            sut.Schedule,
            sut.Ads,
            sut.Charity,
            sut.Goals,
            sut.HypeTrain,
            sut.Teams,
            sut.Games,
            sut.ContentClassification,
            sut.Whispers,
            sut.GuestStar,
        ];

        exposed.Should().OnlyHaveUniqueItems().And.HaveCount(26);
    }
}
