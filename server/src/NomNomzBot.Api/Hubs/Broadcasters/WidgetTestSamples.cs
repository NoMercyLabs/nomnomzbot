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
using NomNomzBot.Domain.Identity;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Games.Frames;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// One sample payload per widget event type, for the dashboard's "test this overlay" action. Each sample is an
/// instance of the exact type the live handler in this folder sends, so a test fire carries every field the
/// stream does and a widget that works in the test works live. Only the live-game frames stay hand-shaped: each
/// game builds its own frame object, so there is no one type to share.
/// </summary>
internal static class WidgetTestSamples
{
    private const string AvatarUrl =
        "https://static-cdn.jtvnw.net/user-default-pictures-uv/13e5fa74-defa-11e9-809c-784f43822e80-profile_image-300x300.png";
    private const string Pronouns = "They/Them";
    private static readonly string Standing = nameof(CommunityStanding.Subscriber);

    private const string PollTitle = "What game next?";
    private const string PredictionTitle = "Will we beat the boss this attempt?";

    private static readonly IReadOnlyList<HypeTrainContributionDto> HypeTrainContributions =
    [
        new("100000017", "topcheerer", "TopCheerer", "bits", 800),
        new("100000018", "topgifter", "TopGifter", "subscription", 500),
    ];

    private static readonly IReadOnlyList<PredictionOutcomeDto> PredictionOutcomes =
    [
        new("o1", "Yes", 12000, 42, "BLUE"),
        new("o2", "No", 4500, 18, "PINK"),
    ];

    /// <summary>Every event type that has a sample: what the dashboard's Test menu and the editor preview offer.</summary>
    public static IReadOnlyCollection<string> EventTypes => Samples.Keys;

    /// <summary>The sample for <paramref name="eventType"/>, or a bare <c>{ user }</c> for a type nothing sends live
    /// (a custom widget's own event name).</summary>
    public static object For(string eventType, DateTimeOffset now) =>
        Samples.TryGetValue(eventType, out Func<DateTimeOffset, object>? sample)
            ? sample(now)
            : Fallback();

    /// <summary>The key under which <see cref="Table"/> carries the fallback sample.</summary>
    public const string FallbackKey = "_default";

    /// <summary>Every sample by event type, plus the fallback under <see cref="FallbackKey"/>: the one table the
    /// editor preview fires from, so it sends exactly what the Test button does.</summary>
    public static Dictionary<string, object> Table(DateTimeOffset now)
    {
        Dictionary<string, object> table = Samples.ToDictionary(
            pair => pair.Key,
            pair => pair.Value(now)
        );
        table[FallbackKey] = Fallback();
        return table;
    }

    private static object Fallback() => new { user = "TestUser" };

    private static readonly Dictionary<string, Func<DateTimeOffset, object>> Samples = new()
    {
        ["follow"] = now => new FollowAlertDto(
            "100000001",
            "TestFollower",
            "testfollower",
            now,
            AvatarUrl,
            Pronouns,
            Standing
        ),
        ["subscription"] = _ => new SubscriptionAlertDto("100000002", "TestSubscriber", "1000"),
        ["resub"] = _ => new ResubAlertDto(
            "100000003",
            "TestResubber",
            "1000",
            6,
            3,
            "Six months already!"
        ),
        ["gift"] = _ => new GiftSubAlertDto("100000004", "TestGifter", "1000", 5, false),
        ["cheer"] = _ => new CheerAlertDto(
            "100000005",
            "TestCheerer",
            500,
            "Cheer500 Great stream!",
            false
        ),
        ["raid"] = _ => new RaidAlertDto("100000006", "TestRaider", "testraider", 42),
        ["ban"] = now => ModAction("ban", "Spamming links", null, now),
        ["timeout"] = now => ModAction("timeout", "Calm down for a bit", 600, now),
        ["unban"] = now => ModAction("unban", null, null, now),
        ["ChatMessage"] = now => ChatMessage(now),
        ["ChatCleared"] = _ => new ChatClearedDto("100000010"),
        ["MessageDeleted"] = _ => new MessageDeletedDto("test-message", "100000010", "100000011"),
        ["UserMessagesCleared"] = _ => new UserMessagesClearedDto(
            "100000011",
            "TestChatter",
            "testchatter"
        ),
        // chat_box.vue's onEnriched returns without a title, so the sample carries a full song-link card.
        ["ChatMessageEnriched"] = _ => new ChatMessageEnrichedWidgetPayload(
            "test-message",
            "https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC",
            "Never Gonna Give You Up",
            "Rick Astley",
            "https://i.scdn.co/image/ab67616d0000b2734cd0672c1e1a9b4b8f5e1f32",
            "spotify",
            "TestViewer",
            "testviewer"
        ),
        ["hype_train_begin"] = now => new HypeTrainBeganAlertDto(
            "test-hype-train",
            2,
            1350,
            350,
            1000,
            HypeTrainContributions,
            now.AddMinutes(5)
        ),
        ["hype_train_progress"] = now => new HypeTrainProgressAlertDto(
            "test-hype-train",
            2,
            1350,
            350,
            1000,
            HypeTrainContributions,
            now.AddMinutes(5)
        ),
        ["hype_train_end"] = now => new HypeTrainEndedAlertDto(
            "test-hype-train",
            3,
            2800,
            HypeTrainContributions,
            now
        ),
        ["poll_begin"] = now => new PollBeganAlertDto(
            "test-poll",
            PollTitle,
            PollChoices(42),
            120,
            now.AddMinutes(2)
        ),
        ["poll_progress"] = now => new PollProgressAlertDto(
            "test-poll",
            PollTitle,
            PollChoices(42),
            now.AddMinutes(2)
        ),
        ["poll_end"] = _ => new PollEndedAlertDto(
            "test-poll",
            PollTitle,
            "completed",
            PollChoices(62),
            "c1"
        ),
        ["prediction_begin"] = now => new PredictionBeganAlertDto(
            "test-pred",
            PredictionTitle,
            PredictionOutcomes,
            120,
            now.AddMinutes(2)
        ),
        ["prediction_progress"] = now => new PredictionProgressAlertDto(
            "test-pred",
            PredictionTitle,
            PredictionOutcomes,
            now.AddMinutes(2)
        ),
        ["prediction_lock"] = _ => new PredictionLockedAlertDto(
            "test-pred",
            PredictionTitle,
            PredictionOutcomes
        ),
        ["prediction_end"] = _ => new PredictionEndedAlertDto(
            "test-pred",
            PredictionTitle,
            "resolved",
            PredictionOutcomes,
            "o1"
        ),
        ["reward_redeemed"] = now => new RewardRedeemedDto(
            BroadcasterId: "test-channel",
            RewardId: "test-reward",
            RewardTitle: "Hydrate!",
            RedemptionId: "test-redemption",
            UserId: "100000012",
            UserDisplayName: "TestRedeemer",
            Cost: 500,
            UserInput: "Drink some water please!",
            Timestamp: now.ToString("O"),
            AvatarUrl: AvatarUrl,
            Pronouns: Pronouns,
            CommunityStanding: Standing,
            EventId: "test-event"
        ),
        ["moderator_added"] = _ => RoleChanged(),
        ["moderator_removed"] = _ => RoleChanged(),
        ["vip_added"] = _ => RoleChanged(),
        ["vip_removed"] = _ => RoleChanged(),
        ["shoutout_sent"] = _ => new ShoutoutSentAlertDto("100000014", "TestFriend"),
        ["shoutout_received"] = _ => new ShoutoutReceivedAlertDto(
            "100000015",
            "TestFriend",
            "testfriend",
            84,
            AvatarUrl,
            Pronouns,
            Standing
        ),
        ["sr_queue"] = _ => new SrQueueWidgetPayload([
            new SongRequestQueueSnapshotItem("Never Gonna Give You Up", "TestViewer", 213, "K7QM"),
            new SongRequestQueueSnapshotItem("Sandstorm", "AnotherViewer", 225, "B2XR"),
            new SongRequestQueueSnapshotItem("Bohemian Rhapsody", "ThirdViewer", 355, "M9TD"),
        ]),
        ["now_playing"] = now => new NowPlayingWidgetPayload(
            true,
            "Test Track",
            "Test Artist",
            "https://i.scdn.co/image/ab67616d0000b2734cd0672c1e1a9b4b8f5e1f32",
            "spotify",
            "spotify:track:4uLU6hMCjMI75M1A2tKUQC",
            213000,
            42000,
            now,
            "TestViewer"
        ),
        ["ad_schedule"] = now => new AdScheduleWidgetPayload(
            now.AddMinutes(3),
            now.AddMinutes(-57),
            90,
            0,
            3,
            now.AddMinutes(30),
            180
        ),
        ["ad_upcoming"] = now => new AdUpcomingWidgetPayload(120, 120, 90, now.AddMinutes(2)),
        [YouTubePlayerDispatcher.PlayEventType] = _ => new YouTubePlayWidgetPayload(
            "dQw4w9WgXcQ",
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        ),
        [YouTubePlayerDispatcher.PauseEventType] = _ => new YouTubeTransportWidgetPayload(),
        [YouTubePlayerDispatcher.ResumeEventType] = _ => new YouTubeTransportWidgetPayload(),
        [YouTubePlayerDispatcher.StopEventType] = _ => new YouTubeTransportWidgetPayload(),
        [YouTubePlayerDispatcher.SeekEventType] = _ => new YouTubeSeekWidgetPayload(42_000),
        // now_playing.vue's heart pulse reads only isSaved.
        ["track_saved_changed"] = _ => new TrackSavedWidgetPayload(
            "spotify:track:4uLU6hMCjMI75M1A2tKUQC",
            "Test Track",
            "Test Artist",
            true
        ),
        ["tts_speak"] = _ => new TtsSpeakWidgetPayload(
            "Hey streamer, thanks for the awesome content!",
            "en-US-JennyNeural",
            "100000016",
            3200,
            null
        ),
        // The overlay SDK raises these from the raw PlaySound / StopSound hub targets; a clip and its handle pair
        // up, so firing play_sound then stop_sound exercises the same start/stop path a pipeline does.
        ["play_sound"] = _ => new PlaySoundPayload(WidgetTestBeep.DataUri, 80, "preview-sound"),
        ["stop_sound"] = _ => new StopSoundPayload("preview-sound", false),
        // The generic event: what a widget that declares nothing of its own, or a custom event name, receives.
        ["test"] = _ => new
        {
            user = "TestUser",
            message = "This is a test event from the editor preview.",
        },
        // goal_bar defaults to the followers metric.
        ["goal"] = _ => new GoalWidgetEventPayload("followers", 72, 100),
        ["supporter.tip"] = _ =>
            Supporter("tip", "TestTipper", 2500, null, null, "Keep it up!", false),
        ["supporter.membership"] = _ =>
            Supporter("membership", "TestMember", 500, "Gold", null, null, true),
        ["supporter.merch"] = _ => Supporter("merch", "TestBuyer", 3000, null, 2, null, false),
        ["supporter.charity"] = _ =>
            Supporter("charity", "TestDonor", 5000, null, null, "For a good cause", false),
        ["voice_trigger"] = _ => new VoiceTriggerWidgetEventPayload("hello", 3, null),
        // The custom_data widget defaults to source "heartrate", field "bpm".
        ["custom.heartrate"] = _ => new CustomDataWidgetPayload(
            new Dictionary<string, string> { ["bpm"] = "142" }
        ),
        // Live-game frames share a `kind`-discriminated envelope across all four game widgets. The samples are
        // the drop game's frames: the lobby opens, a player drops, the round resolves.
        ["game.lobby"] = _ => new GameRoundOpenFrame
        {
            LobbySeconds = 30,
            Target = 50,
            Radius = 12,
        },
        ["game.running"] = _ => new GameDropFrame
        {
            Player = "TestWinner",
            Landed = 52.4,
            Distance = 2.4,
            Hit = true,
        },
        ["game.resolved"] = _ => new GameResultsFrame
        {
            Target = 50,
            Radius = 12,
            Results =
            [
                new()
                {
                    Player = "TestWinner",
                    Landed = 52.4,
                    Distance = 2.4,
                    Won = true,
                    Payout = 500,
                },
                new()
                {
                    Player = "SecondPlace",
                    Landed = 34,
                    Distance = 16,
                    Won = false,
                    Payout = 0,
                },
            ],
        },
    };

    private static IReadOnlyList<PollChoiceDto> PollChoices(int leaderVotes) =>
        [
            new("c1", "Elden Ring", leaderVotes, 10),
            new("c2", "Minecraft", 28, 5),
            new("c3", "Just Chatting", 15, 0),
        ];

    private static RoleChangedAlertDto RoleChanged() =>
        new("100000013", "TestPromoted", "testpromoted", AvatarUrl, Pronouns, Standing);

    private static ModActionDto ModAction(
        string action,
        string? reason,
        int? durationSeconds,
        DateTimeOffset now
    ) =>
        new(
            action,
            "100000010",
            "100000011",
            reason,
            durationSeconds,
            "TestChatter",
            AvatarUrl,
            Pronouns,
            Standing,
            "TestModerator",
            now
        );

    private static SupporterAlertPayload Supporter(
        string kind,
        string name,
        long amountMinor,
        string? tier,
        int? quantity,
        string? message,
        bool recurring
    ) => new(kind, name, amountMinor, "USD", tier, quantity, message, recurring);

    // A decorated chat line: resolved emote images, a native chat GIF (the branch a Tier 2+ viewer's GIF takes,
    // which a chat box can get wrong while emotes render fine), a broadcaster badge, name colour and pronouns.
    private static DashboardChatMessageDto ChatMessage(DateTimeOffset now) =>
        new(
            Id: "test-message",
            ChannelId: "test-channel",
            UserId: "100000011",
            DisplayName: "TestChatter",
            Username: "testchatter",
            Message: "Hey chat! Kappa this stream is amazing LUL 4Head",
            Fragments:
            [
                Text("Hey chat! "),
                Emote("Kappa", "25"),
                Text(" this stream is amazing "),
                Emote("LUL", "425618"),
                Text(" "),
                Emote("4Head", "354"),
                Gif("[Excited Dance GIF by Giphy]", "l0HlKrB02QY0f1mbm"),
            ],
            UserType: ChatRole.ToToken(PermissionLevel.Broadcaster),
            IsSubscriber: false,
            IsVip: false,
            IsModerator: false,
            IsBroadcaster: true,
            IsCheer: false,
            IsCommand: false,
            Badges:
            [
                new(
                    "broadcaster",
                    "1",
                    null,
                    new Dictionary<string, string>
                    {
                        ["1"] =
                            "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/1",
                        ["2"] =
                            "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/2",
                        ["4"] =
                            "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/3",
                    }
                ),
            ],
            BitsAmount: 0,
            Color: "#9146ff",
            MessageType: "text",
            ReplyToMessageId: null,
            ReplyParentMessageBody: null,
            ReplyParentUserName: null,
            Timestamp: now.ToString("O"),
            AvatarUrl: AvatarUrl,
            Pronouns: Pronouns
        );

    private static ChatFragmentDto Text(string text) =>
        new("text", text, null, null, null, null, null);

    // Twitch resolves a chat GIF against GIPHY and sends the finished url; the caption is what a renderer falls
    // back to without one.
    private static ChatFragmentDto Gif(string caption, string id) =>
        new(
            "gif",
            caption,
            null,
            null,
            null,
            null,
            null,
            new(id, $"https://media.giphy.com/media/{id}/giphy.gif")
        );

    // The v2 emote CDN serves 1.0/2.0/3.0 renditions of a stable global emote id.
    private static ChatFragmentDto Emote(string name, string id) =>
        new(
            "emote",
            name,
            new(
                id,
                null,
                "static",
                "twitch",
                new Dictionary<string, string>
                {
                    ["1"] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/1.0",
                    ["2"] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/2.0",
                    ["3"] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/3.0",
                },
                false,
                false
            ),
            null,
            null,
            null,
            null
        );
}
