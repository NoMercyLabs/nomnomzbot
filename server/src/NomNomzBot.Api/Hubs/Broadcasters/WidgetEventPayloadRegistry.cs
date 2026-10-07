// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Infrastructure.Games.Frames;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The widget event name -> payload type table behind the typed <c>NomNomz.on(...)</c> declarations. It sits beside
/// the broadcasters that send these events; <c>WidgetEventPayloadRegistryDriftTests</c> holds it to
/// <see cref="WidgetTestSamples"/> (same name, same runtime type) and to every name a broadcaster sends. A null type
/// with no variants is a raw SDK-local event; the live-game events list the frame records their widgets narrow on <c>kind</c>.
/// </summary>
internal sealed class WidgetEventPayloadRegistry : IWidgetEventPayloadRegistry
{
    // A live game pushes these while the round is open or running (the lobby/running split is the engine's phase,
    // and a late join or a drop can land in either), and the last two when it ends.
    private static readonly Type[] LiveFrames =
    [
        typeof(GameRoundOpenFrame),
        typeof(GameJoinFrame),
        typeof(GameMultiplierFrame),
        typeof(GameCashoutFrame),
        typeof(GameDropFrame),
    ];

    private static readonly Type[] FinalFrames =
    [
        typeof(GameResultsFrame),
        typeof(GameCancelledFrame),
    ];

    public IReadOnlyList<WidgetEventPayloadEntry> Events { get; } =
    [
        new("follow", typeof(FollowAlertDto)),
        new("subscription", typeof(SubscriptionAlertDto)),
        new("resub", typeof(ResubAlertDto)),
        new("gift", typeof(GiftSubAlertDto)),
        new("cheer", typeof(CheerAlertDto)),
        new("raid", typeof(RaidAlertDto)),
        new("ban", typeof(ModActionDto)),
        new("timeout", typeof(ModActionDto)),
        new("unban", typeof(ModActionDto)),
        new("ChatMessage", typeof(DashboardChatMessageDto)),
        new("ChatCleared", typeof(ChatClearedDto)),
        new("MessageDeleted", typeof(MessageDeletedDto)),
        new("UserMessagesCleared", typeof(UserMessagesClearedDto)),
        new("ChatMessageEnriched", typeof(ChatMessageEnrichedWidgetPayload)),
        new("hype_train_begin", typeof(HypeTrainBeganAlertDto)),
        new("hype_train_progress", typeof(HypeTrainProgressAlertDto)),
        new("hype_train_end", typeof(HypeTrainEndedAlertDto)),
        new("poll_begin", typeof(PollBeganAlertDto)),
        new("poll_progress", typeof(PollProgressAlertDto)),
        new("poll_end", typeof(PollEndedAlertDto)),
        new("prediction_begin", typeof(PredictionBeganAlertDto)),
        new("prediction_progress", typeof(PredictionProgressAlertDto)),
        new("prediction_lock", typeof(PredictionLockedAlertDto)),
        new("prediction_end", typeof(PredictionEndedAlertDto)),
        new("reward_redeemed", typeof(RewardRedeemedDto)),
        new("moderator_added", typeof(RoleChangedAlertDto)),
        new("moderator_removed", typeof(RoleChangedAlertDto)),
        new("vip_added", typeof(RoleChangedAlertDto)),
        new("vip_removed", typeof(RoleChangedAlertDto)),
        new("shoutout_sent", typeof(ShoutoutSentAlertDto)),
        new("shoutout_received", typeof(ShoutoutReceivedAlertDto)),
        new("sr_queue", typeof(SrQueueWidgetPayload)),
        new("now_playing", typeof(NowPlayingWidgetPayload)),
        new("ad_schedule", typeof(AdScheduleWidgetPayload)),
        new("ad_upcoming", typeof(AdUpcomingWidgetPayload)),
        new(YouTubePlayerDispatcher.PlayEventType, typeof(YouTubePlayWidgetPayload)),
        new(YouTubePlayerDispatcher.PauseEventType, typeof(YouTubeTransportWidgetPayload)),
        new(YouTubePlayerDispatcher.ResumeEventType, typeof(YouTubeTransportWidgetPayload)),
        new(YouTubePlayerDispatcher.StopEventType, typeof(YouTubeTransportWidgetPayload)),
        new(YouTubePlayerDispatcher.SeekEventType, typeof(YouTubeSeekWidgetPayload)),
        new("track_saved_changed", typeof(TrackSavedWidgetPayload)),
        new("tts_speak", typeof(TtsSpeakWidgetPayload)),
        new("goal", typeof(GoalWidgetEventPayload)),
        new("count", typeof(CountWidgetEventPayload)),
        new("supporter.tip", typeof(SupporterAlertPayload)),
        new("supporter.membership", typeof(SupporterAlertPayload)),
        new("supporter.merch", typeof(SupporterAlertPayload)),
        new("supporter.charity", typeof(SupporterAlertPayload)),
        new("voice_trigger", typeof(VoiceTriggerWidgetEventPayload)),
        new("game.lobby", null, LiveFrames),
        new("game.running", null, LiveFrames),
        new("game.resolved", null, FinalFrames),
        // Raised by the overlay SDK itself from the raw PlaySound / StopSound hub targets; the hub sends these records.
        new("play_sound", typeof(PlaySoundPayload)),
        new("stop_sound", typeof(StopSoundPayload)),
        // The generic event: free-form, fired at a widget that declares no event of its own.
        new("test", null),
    ];

    public Type CustomEventPayloadType => typeof(CustomDataWidgetPayload);
}
