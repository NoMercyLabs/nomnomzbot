// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// The immutable per-widget metadata for one first-party overlay widget. <c>SourceCode</c> is NOT here — it is
/// read at seed time from the embedded SFC asset keyed by <see cref="Key"/>. This is the single source of truth
/// for the first-party catalogue: both <see cref="FirstPartyWidgetCatalogueSeeder"/> (which seeds the gallery
/// rows) and <see cref="WidgetSettingsSchemaProvider"/> (which authors the typed settings schema) read it, so a
/// key + its default settings can never drift between the two.
/// </summary>
public sealed record FirstPartyWidgetDefinition(
    string Key,
    string Name,
    string Description,
    Dictionary<string, object> DefaultSettings,
    List<string> DefaultEventSubscriptions
);

/// <summary>
/// The 21 first-party overlay widgets shipped in-repo (widgets-overlays.md). Each declares its <c>key</c>, display
/// metadata, the settings keys + defaults its Vue <c>cfg</c> honours, and the event topics it subscribes to by
/// default. Kept as one static list so the seeder and the settings-schema provider share exactly one definition.
/// </summary>
public static class FirstPartyWidgetCatalogue
{
    /// <summary>Every Twitch + supporter alert type the alerts widget and ticker default to listening for.</summary>
    public static readonly string[] SupporterAndTwitchEvents =
    [
        "follow",
        "subscription",
        "resub",
        "gift",
        "cheer",
        "raid",
        "supporter.tip",
        "supporter.membership",
        "supporter.merch",
        "supporter.charity",
    ];

    public static readonly IReadOnlyList<FirstPartyWidgetDefinition> All =
    [
        new(
            Key: "alerts",
            Name: "Alerts",
            Description: "A one-at-a-time queue of animated alert cards for follows, subs, resubs, gift subs, cheers, "
                + "raids, and supporter events, with per-type amount thresholds and a custom text template.",
            DefaultSettings: new()
            {
                ["events"] = new List<string>(SupporterAndTwitchEvents),
                ["textTemplate"] = "",
                ["durationMs"] = 6000,
                ["minBits"] = 0,
                ["minGiftCount"] = 0,
                ["minAmount"] = 0,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: new(SupporterAndTwitchEvents)
        ),
        new(
            Key: "goal_bar",
            Name: "Goal Bar",
            Description: "An animated progress bar toward a follower, sub, or bits goal. Reads the authoritative total from "
                + "goal events and live-increments on matching follows, subs, gifts, and cheers.",
            DefaultSettings: new()
            {
                ["metric"] = "followers",
                ["target"] = 100,
                ["start"] = 0,
                ["resetCadence"] = "",
                ["colors"] = new Dictionary<string, object>(),
                ["labels"] = new Dictionary<string, object>(),
            },
            DefaultEventSubscriptions: ["goal", "follow", "subscription", "gift", "cheer"]
        ),
        new(
            Key: "labels",
            Name: "Labels",
            Description: "A single live stat rendered as styled text — latest follower, latest sub, top cheerer, follower "
                + "count, or sub count — with an optional format string.",
            DefaultSettings: new()
            {
                ["label"] = "latest_follower",
                ["formatString"] = "",
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["follow", "subscription", "resub", "gift", "cheer", "goal"]
        ),
        new(
            Key: "drop_game",
            Name: "Drop Game",
            Description: "The live drop-game round on a horizontal track: the target zone, every chatter's landing "
                + "marker as they type !drop, and the payout scoreboard when the round resolves.",
            DefaultSettings: new() { ["accentColor"] = "#9146ff", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "raffle",
            Name: "Raffle",
            Description: "The live raffle round: the entrant roster and climbing pot as chatters type !raffle, and "
                + "the winner reveal + payout board when one entrant takes the whole pot.",
            DefaultSettings: new() { ["accentColor"] = "#9146ff", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "heist",
            Name: "Heist",
            Description: "The live heist round: the crew roster and rising escape odds as chatters type !heist, and "
                + "each member's getaway outcome + payout when the job resolves.",
            DefaultSettings: new() { ["accentColor"] = "#9146ff", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "crash",
            Name: "Crash",
            Description: "The live crash round: a big rising multiplier readout, the cash-out ticker as chatters "
                + "!crash to lock in their multiplier, and the bust/max reveal + payout board.",
            DefaultSettings: new() { ["accentColor"] = "#9146ff", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "event_ticker",
            Name: "Event Ticker",
            Description: "A horizontal scrolling ticker of recent channel events as compact chips, newest appended and the "
                + "oldest retired past a retained count, at a configurable speed.",
            DefaultSettings: new()
            {
                ["events"] = new List<string>(SupporterAndTwitchEvents),
                ["speed"] = 60,
                ["count"] = 20,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: new(SupporterAndTwitchEvents)
        ),
        new(
            Key: "chat_box",
            Name: "Chat Box",
            Description: "Live chat rendered from the decorated message feed — resolved emotes, badges, name colours, "
                + "and pronouns — with theming, font/size/background controls, timestamps, command/bot filtering, and optional per-message fade.",
            DefaultSettings: new()
            {
                ["theme"] = "dark",
                ["fontFamily"] = "",
                ["fontSize"] = 16,
                ["background"] = "",
                ["backgroundOpacity"] = 0.82,
                ["showTimestamps"] = false,
                ["maxMessages"] = 12,
                ["fadeAfterMs"] = 0,
                ["showBadges"] = true,
                ["showEmotes"] = true,
                ["hideCommands"] = true,
                ["hideBots"] = true,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["ChatMessage"]
        ),
        new(
            Key: "now_playing",
            Name: "Now Playing",
            Description: "A standing display of the current track — pill or card layout, optional album art and an "
                + "animated progress sweep — that hides itself while nothing plays.",
            DefaultSettings: new()
            {
                ["layout"] = "pill",
                ["showArt"] = true,
                ["showProgressBar"] = true,
                ["provider"] = "",
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["now_playing"]
        ),
        new(
            Key: "sr_queue",
            Name: "SR Queue",
            Description: "The upcoming song-request queue as a compact list — position, title, and optional requester "
                + "and duration — fed by sr_queue snapshot events and hidden while the queue is empty.",
            DefaultSettings: new()
            {
                ["count"] = 5,
                ["showRequester"] = true,
                ["showDuration"] = true,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["sr_queue"]
        ),
        new(
            Key: "tts_caption",
            Name: "TTS Caption",
            Description: "A speaking indicator with a live caption for TTS playback — animated voice bars, the "
                + "speaker's name, optional voice label, top or bottom placement — hidden while nothing speaks.",
            DefaultSettings: new()
            {
                ["showText"] = true,
                ["voiceLabel"] = false,
                ["position"] = "bottom",
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["tts_speak"]
        ),
        new(
            Key: "poll_prediction",
            Name: "Poll / Prediction",
            Description: "Live poll and prediction bars — choices with vote percentages, lock state, and the winning "
                + "outcome highlighted on resolve — shown only while a round runs.",
            DefaultSettings: new()
            {
                ["position"] = "left",
                ["colors"] = new Dictionary<string, object>(),
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions:
            [
                "poll_begin",
                "poll_progress",
                "poll_end",
                "prediction_begin",
                "prediction_progress",
                "prediction_lock",
                "prediction_end",
            ]
        ),
        new(
            Key: "redemption_alert",
            Name: "Redemption Alert",
            Description: "A channel-point redemption popup — one card at a time with the redeemer, reward, cost, and "
                + "their input — filterable per reward and templatable.",
            DefaultSettings: new()
            {
                ["rewards"] = new List<string>(),
                ["textTemplate"] = "",
                ["sound"] = "",
                ["durationMs"] = 6000,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["reward_redeemed"]
        ),
        new(
            Key: "countdown_timer",
            Name: "Countdown / Timer",
            Description: "A countdown to a wall-clock target or for a fixed duration (BRB / starting soon) with a "
                + "label and completion text — dashboard-controlled live through its widget settings.",
            DefaultSettings: new()
            {
                ["target"] = "",
                ["durationMs"] = 0,
                ["label"] = "Starting soon",
                ["onCompleteText"] = "",
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: []
        ),
        new(
            Key: "emote_wall",
            Name: "Emote Wall",
            Description: "Emotes from chat float or rain across the screen — harvested from the decorated message "
                + "feed's emote and cheermote fragments, with density, size, and provider filters.",
            DefaultSettings: new()
            {
                ["density"] = 30,
                ["size"] = 48,
                ["animation"] = "float",
                ["providers"] = new List<string>(),
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["ChatMessage"]
        ),
        new(
            Key: "custom_data",
            Name: "Custom Data",
            Description: "The live value of a custom data source rendered as a number, gauge, or text — a heart-rate "
                + "gauge is this widget bound to heartrate.bpm — re-binding live when the source changes.",
            DefaultSettings: new()
            {
                ["source"] = "heartrate",
                ["field"] = "bpm",
                ["render"] = "number",
                ["label"] = "",
                ["min"] = 0,
                ["max"] = 200,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["custom.heartrate"]
        ),
        new(
            Key: "recent_followers",
            Name: "Recent Followers",
            Description: "A persistent standings panel of the most recent followers, newest on top — a always-on "
                + "list rather than a one-at-a-time alert, with a configurable count and title.",
            DefaultSettings: new()
            {
                ["count"] = 5,
                ["title"] = "Recent followers",
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["follow"]
        ),
        new(
            Key: "sub_train",
            Name: "Sub Train",
            Description: "A hype counter that spikes on rapid subs and gift subs and cools down on its own — the "
                + "count of subs still inside a rolling window (a gift of N counts as N), hidden when the train stops.",
            DefaultSettings: new() { ["windowMs"] = 300000, ["accentColor"] = "#9146ff" },
            DefaultEventSubscriptions: ["subscription", "resub", "gift"]
        ),
        new(
            Key: "socials",
            Name: "Socials",
            Description: "A config-only rotating handles bar — the streamer's social accounts cross-faded through at "
                + "a configurable interval. No event feed; the handles come from the widget settings.",
            DefaultSettings: new()
            {
                ["handles"] = new List<object>(),
                ["rotateMs"] = 8000,
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: []
        ),
        new(
            Key: "top_cheerers",
            Name: "Top Cheerers",
            Description: "A ranked board of the session's biggest cheerers by bits — the full leaderboard the labels "
                + "widget only hints at with its single top cheerer, with a configurable depth and title.",
            DefaultSettings: new()
            {
                ["count"] = 5,
                ["title"] = "Top cheerers",
                ["accentColor"] = "#9146ff",
            },
            DefaultEventSubscriptions: ["cheer"]
        ),
    ];
}
