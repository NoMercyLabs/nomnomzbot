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

using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Stage;

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
    List<string> DefaultEventSubscriptions,
    ObsBrowserSource ObsSetup
)
{
    /// <summary>
    /// Translation key of the display name, derived from <see cref="Key"/> so the pair can never drift from the
    /// entry. <see cref="Name"/> stays as the English fallback for a client that does not know the key.
    /// </summary>
    public string NameKey => $"widget.gallery.{Key}.name";

    /// <summary>Translation key of the description (see <see cref="NameKey"/>).</summary>
    public string DescriptionKey => $"widget.gallery.{Key}.description";
}

/// <summary>
/// The 23 first-party overlay widgets shipped in-repo (widgets-overlays.md). Each declares its <c>key</c>, display
/// metadata, the settings keys + defaults its Vue <c>cfg</c> honours, and the event topics it subscribes to by
/// default. Kept as one static list so the seeder and the settings-schema provider share exactly one definition.
/// </summary>
public static class FirstPartyWidgetCatalogue
{
    /// <summary>
    /// Natural keys of catalogue entries that are actually channel-owned system surfaces (widgets-overlays.md
    /// §1.2), not browsable/installable gallery items — <c>tts_caption</c> and <c>alerts</c> are each provisioned
    /// automatically by <c>IWidgetService.EnsureSystemWidgetAsync</c> the moment their owner page (TTS / Alerts
    /// &amp; Events) needs them, never chosen from the gallery. The third system surface in §1.2 (Sound) has no
    /// distinct catalogue entry yet, so it is not listed here. The <c>WidgetGalleryItem</c> rows still exist (the
    /// seeder still upserts them, and <c>EnsureSystemWidgetAsync</c> looks each up by natural key) — this set only
    /// excludes them from the user-facing browse/search listing.
    /// </summary>
    public static readonly IReadOnlyCollection<string> SystemSurfaceNaturalKeys =
    [
        "tts_caption",
        // The stage: full-screen one-off effects (run across, confetti, shake, flash) a command or pipeline triggers.
        StageSurface.WidgetKey,
        WidgetKeyAliases.Audio,
        WidgetKeyAliases.MusicPlayer,
        "alerts",
        // The media share player: without it no approved clip or video plays on stream.
        "media_share",
    ];

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

    /// <summary>
    /// A visual overlay at its own smallest real size: the page lays out inside the browser source, anchored to a
    /// corner, an edge or the centre of that source with a 4 px margin (so the card's shadow is not clipped), and its
    /// widths are fixed pixel values, never a share of a 1920 x 1080 screen. A source made earlier at 1920 x 1080
    /// still looks right: the widget stays in the same corner, edge or centre, at its natural size. OBS audio
    /// control stays off: none plays sound. Shutdown stays off: OBS would unload the page while hidden, so it would
    /// miss events and lose what it holds (a queue, a round, a rolling window, the chat so far).
    /// </summary>
    private static ObsBrowserSource Overlay(int width, int height) =>
        new(width, height, false, false);

    /// <summary>
    /// The overlays whose content really spans the screen, so their source is the whole 1920 x 1080 canvas: the
    /// emote wall floats emotes the full screen height, the stage runs text and confetti edge to edge, the drop
    /// game drops parachutes down the screen onto a ground that spans its width, and the raid recap is a full
    /// scene whose type sizes follow the 1920 x 1080 frame. Every other drawing widget declares a smaller size.
    /// </summary>
    private static readonly ObsBrowserSource FullCanvas = new(1920, 1080, false, false);

    /// <summary>
    /// A source that only plays sound and draws nothing: 1 x 1 (its root fills the source and the optional
    /// indicator is 12 px). OBS audio control is ticked so the sound reaches the stream's mixer. Shutdown stays off:
    /// a shut-down source stops playing and listening.
    /// </summary>
    private static readonly ObsBrowserSource SoundOnly = new(1, 1, true, false);

    /// <summary>Widgets that draw only their content (the emote wall's emotes): no theme parts, so no look to pick.</summary>
    private static readonly HashSet<string> Unthemed = new(StringComparer.Ordinal) { "emote_wall" };

    /// <summary>
    /// The overlays that draw and play sound: the TTS Caption shows the words and plays the spoken message itself,
    /// and Media Share plays the video's own sound, so OBS audio control is ticked. Shutdown stays off for the same
    /// reason as the rest.
    /// </summary>
    private static ObsBrowserSource OverlayWithSound(int width, int height) =>
        new(width, height, true, false);

    /// <summary>The setting every widget that draws carries: which look of the overlay theme it wears.</summary>
    public const string ThemeSettingKey = "overlayTheme";

    /// <summary>The looks in <c>Content/Widgets/Theme/overlay-theme.css</c>; the first is the default.</summary>
    public static readonly IReadOnlyList<string> Themes = ["sticker", "ink", "print", "classic"];

    /// <summary>
    /// The one main color of each look, the value its <c>--nnz-accent</c> (or, for print, <c>--nnz-surface</c>) holds in
    /// <c>overlay-theme.css</c>. The color setting changes exactly this variable, and its default is the main color of
    /// the default look, so the picker shows the real color. The overlay SDK holds the same table; a test keeps the
    /// two equal.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ThemeMainColors = new Dictionary<
        string,
        string
    >
    {
        ["sticker"] = "#ff2d6f",
        ["ink"] = "#c8ff2e",
        ["print"] = "#d4ff3a",
        ["classic"] = "#9146ff",
    };

    /// <summary>The color setting every widget with an accent carries.</summary>
    public const string AccentSettingKey = "accentColor";

    /// <summary>Where the overlay sits in its browser source: one of <see cref="Anchors"/>.</summary>
    public const string AnchorSettingKey = "anchor";

    /// <summary>The distance in pixels between the overlay and the browser source edge it is anchored to.</summary>
    public const string EdgeOffsetSettingKey = "edgeOffset";

    /// <summary>The size of the overlay in percent of its natural size.</summary>
    public const string ScaleSettingKey = "scale";

    /// <summary>The browser source width the overlay is drawn for (read only; the page zooms to fill another size).</summary>
    public const string DesignWidthSettingKey = "designWidth";

    /// <summary>The browser source height the overlay is drawn for (read only).</summary>
    public const string DesignHeightSettingKey = "designHeight";

    /// <summary>The nine places an overlay can sit in its browser source.</summary>
    public static readonly IReadOnlyList<string> Anchors =
    [
        "top-left",
        "top",
        "top-right",
        "left",
        "center",
        "right",
        "bottom-left",
        "bottom",
        "bottom-right",
    ];

    /// <summary>The distance from the source edge every overlay starts with, so a card's shadow is not clipped.</summary>
    public const int DefaultEdgeOffset = 4;

    /// <summary>The scale in percent every overlay starts with.</summary>
    public const int DefaultScale = 100;

    public const int MinScale = 50;
    public const int MaxScale = 200;

    /// <summary>
    /// The 21 sized overlays and the place each one sits today, which is the default of its anchor. The four
    /// full-screen overlays and the two sound-only ones are not here: they have no place to choose.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultAnchors = new Dictionary<
        string,
        string
    >
    {
        ["alerts"] = "top",
        ["goal_bar"] = "bottom",
        ["labels"] = "bottom-left",
        ["raffle"] = "bottom",
        ["heist"] = "bottom",
        ["crash"] = "bottom",
        ["event_ticker"] = "center",
        ["chat_box"] = "bottom-left",
        ["now_playing"] = "bottom-left",
        ["sr_queue"] = "top-right",
        ["media_share"] = "center",
        ["tts_caption"] = "bottom",
        ["poll_prediction"] = "left",
        ["redemption_alert"] = "top",
        ["countdown_timer"] = "center",
        ["custom_data"] = "bottom-right",
        ["recent_followers"] = "top-left",
        ["sub_train"] = "top",
        ["socials"] = "bottom-right",
        ["top_cheerers"] = "top-right",
        ["lucky_feather"] = "bottom",
    };

    /// <summary>
    /// The alert card sat 12% (86 px of 720) below the top, not 4 px, so its edge offset starts there and the card
    /// does not move for a channel that never touches the setting.
    /// </summary>
    private const int AlertsDefaultEdgeOffset = 86;

    private static readonly FirstPartyWidgetDefinition[] Authored =
    [
        new(
            Key: "alerts",
            // The card is up to 600 wide and a sticker alert lands at a random spot in the middle band of the source.
            ObsSetup: Overlay(1280, 720),
            Name: "Alerts",
            Description: "A one-at-a-time queue of animated alert cards for follows, subs, resubs, gift subs, cheers, "
                + "raids, and supporter events, with per-type amount thresholds and a custom text template.",
            DefaultSettings: new()
            {
                ["events"] = new List<string>(SupporterAndTwitchEvents),
                ["textTemplate"] = "",
                ["durationMs"] = 6000,
                ["stickerDurationMs"] = 6000,
                ["minBits"] = 0,
                ["minGiftCount"] = 0,
                ["minAmount"] = 0,
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: [.. SupporterAndTwitchEvents]
        ),
        new(
            Key: "goal_bar",
            // One 520 px card (drawn at 1.45x) holding a title row, the bar and a foot line.
            ObsSetup: Overlay(780, 180),
            Name: "Goal Bar",
            Description: "An animated progress bar toward a follower, sub, or bits goal. Reads the authoritative total from "
                + "goal events and live-increments on matching follows, subs, gifts, and cheers.",
            DefaultSettings: new()
            {
                ["metric"] = "followers",
                ["target"] = 100,
                ["start"] = 0,
                ["startDate"] = "",
                ["resetCadence"] = "",
                ["colors"] = new Dictionary<string, object>(),
                ["labels"] = new Dictionary<string, object>(),
            },
            DefaultEventSubscriptions: ["goal", "follow", "subscription", "gift", "cheer"]
        ),
        new(
            Key: "labels",
            // One tilted pill with a 26 px line of text, room for a 25-character name.
            ObsSetup: Overlay(640, 120),
            Name: "Labels",
            Description: "A single live stat rendered as styled text — latest follower, latest sub, top cheerer, follower "
                + "count, or sub count — with an optional format string.",
            DefaultSettings: new()
            {
                ["label"] = "latest_follower",
                ["formatString"] = "",
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions:
            [
                "follow",
                "subscription",
                "resub",
                "gift",
                "cheer",
                "goal",
                "count",
            ]
        ),
        new(
            Key: "drop_game",
            // Full screen: parachutes fall the whole height onto a ground that spans the width (sizes follow vh and vw).
            ObsSetup: FullCanvas,
            Name: "Drop Game",
            Description: "The live drop-game round on a horizontal track: the target zone, every chatter's landing "
                + "marker as they type !drop, and the payout scoreboard when the round resolves.",
            DefaultSettings: new() { ["accentColor"] = "", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "raffle",
            // One 720 px card (1.45x) with a header and a wrapping roster of entrants.
            ObsSetup: Overlay(1070, 420),
            Name: "Raffle",
            Description: "The live raffle round: the entrant roster and climbing pot as chatters type !raffle, and "
                + "the winner reveal + payout board when one entrant takes the whole pot.",
            DefaultSettings: new() { ["accentColor"] = "", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "heist",
            // One 720 px card (1.45x) with a header and a wrapping roster of crew members.
            ObsSetup: Overlay(1070, 420),
            Name: "Heist",
            Description: "The live heist round: the crew roster and rising escape odds as chatters type !heist, and "
                + "each member's getaway outcome + payout when the job resolves.",
            DefaultSettings: new() { ["accentColor"] = "", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "crash",
            // One 560 px card (1.45x): a 64 px multiplier, the cash-out chips, then the payout board.
            ObsSetup: Overlay(840, 560),
            Name: "Crash",
            Description: "The live crash round: a big rising multiplier readout, the cash-out ticker as chatters type "
                + "!crash to lock in their multiplier, and the bust/max reveal + payout board.",
            DefaultSettings: new() { ["accentColor"] = "", ["hideAfterMs"] = 12000 },
            DefaultEventSubscriptions: ["game.lobby", "game.running", "game.resolved"]
        ),
        new(
            Key: "event_ticker",
            // A one-line bar that fills its source; the width is only the length of the scroll lane.
            ObsSetup: Overlay(1280, 110),
            Name: "Event Ticker",
            Description: "A horizontal scrolling ticker of recent channel events as compact chips, newest appended and the "
                + "oldest retired past a retained count, at a configurable speed.",
            DefaultSettings: new()
            {
                ["events"] = new List<string>(SupporterAndTwitchEvents),
                ["speed"] = 60,
                ["count"] = 20,
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: [.. SupporterAndTwitchEvents]
        ),
        new(
            Key: "chat_box",
            // A 420 px column (1.45x) that stacks the default 5 messages from the bottom. The height is what
            // FirstPartyWidgetObsSizing gives at the default settings: 5 messages of 111 px, plus the padding.
            ObsSetup: Overlay(660, 581),
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
                ["maxMessages"] = 5,
                ["fadeAfterMs"] = 0,
                ["showAvatar"] = true,
                ["showBadges"] = true,
                ["showRoleBadge"] = false,
                ["showEmotes"] = true,
                // Default true — matches the always-on behaviour before this toggle existed, so shipping it is
                // never a silent regression for a channel already relying on painted names.
                ["showSevenTvPaints"] = true,
                ["hideCommands"] = true,
                ["hideBots"] = true,
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions:
            [
                "ChatMessage",
                "ChatMessageEnriched",
                "ChatCleared",
                "MessageDeleted",
                "UserMessagesCleared",
            ]
        ),
        new(
            Key: "now_playing",
            // The default pill (art, a marquee title, artist, requester) is at most 440 px wide (1.45x).
            ObsSetup: Overlay(680, 180),
            Name: "Now Playing",
            Description: "A standing display of the current track — pill or card layout, optional album art and an "
                + "animated progress sweep — that hides itself while nothing plays. Adapts to whichever provider is "
                + "playing. Spotify and YouTube audio plays from the Music Player widget.",
            DefaultSettings: new()
            {
                ["layout"] = "pill",
                ["showArt"] = true,
                ["showProgressBar"] = true,
                ["provider"] = "",
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: ["music.nowPlaying", "track_saved_changed"]
        ),
        new(
            Key: "sr_queue",
            // One 360 px card (1.45x) with a heading and the default 5 rows.
            ObsSetup: Overlay(550, 370),
            Name: "SR Queue",
            Description: "The upcoming song-request queue as a compact list — position, title, and optional requester "
                + "and duration — fed by sr_queue snapshot events and hidden while the queue is empty.",
            DefaultSettings: new()
            {
                ["count"] = 5,
                ["showPosition"] = true,
                ["showCode"] = true,
                ["showRequester"] = true,
                ["showDuration"] = true,
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: ["sr_queue"]
        ),
        new(
            Key: "media_share",
            // Its root fills the source: a 480p video frame (854 x 480) plus the title line below it.
            ObsSetup: OverlayWithSound(854, 520),
            Name: "Media Share",
            Description: "Plays the approved viewer-shared Twitch clips and YouTube videos on stream. The server sends "
                + "load, play, pause, stop, seek and volume commands and this overlay reports its player state back, "
                + "so an ended item moves the queue on. Hidden while nothing plays.",
            DefaultSettings: new() { ["showTitle"] = true, ["accentColor"] = "" },
            DefaultEventSubscriptions: ["media_share_command"]
        ),
        new(
            Key: WidgetKeyAliases.Audio,
            ObsSetup: SoundOnly,
            Name: "Bot Audio",
            Description: "Sound clips and redemption sounds come out of this one browser source; spoken messages (TTS) "
                + "play only from the TTS Caption source. Add it once, size it 1x1, and leave it — keep "
                + "\"Control audio via OBS\" ON or the stream will not hear it. Renders nothing; the optional "
                + "indicator is only for confirming the source is alive during setup. Music plays from the Music Player widget.",
            DefaultSettings: new() { ["showIndicator"] = false, ["accentColor"] = "" },
            DefaultEventSubscriptions: ["tts_speak"]
        ),
        new(
            Key: WidgetKeyAliases.MusicPlayer,
            ObsSetup: SoundOnly,
            Name: "Music Player",
            Description: "Spotify and YouTube music plays here, at its own level. Add it once, size it 1x1, and leave it — keep "
                + "\"Control audio via OBS\" ON or the stream will not hear it. Renders nothing; the optional "
                + "indicator is only for confirming the source is alive during setup. "
                + "To hear music at a different level, set the Music source to Monitor and Output in OBS.",
            DefaultSettings: new()
            {
                ["volume"] = 100,
                ["muted"] = false,
                ["showIndicator"] = false,
                ["accentColor"] = "",
                ["enableAudio"] = true,
            },
            DefaultEventSubscriptions: ["now_playing"]
        ),
        new(
            Key: "tts_caption",
            // One pill up to 700 px wide (1.45x) with a caption of up to three lines.
            ObsSetup: OverlayWithSound(1040, 200),
            Name: "TTS Caption",
            Description: "Spoken messages (TTS) play from this browser source — keep \"Control audio via OBS\" ON. "
                + "Shows a speaking indicator with a live caption — animated voice bars, the "
                + "speaker's name, optional voice label, top or bottom placement — hidden while nothing speaks.",
            DefaultSettings: new()
            {
                ["showText"] = true,
                ["voiceLabel"] = false,
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: ["tts_speak"]
        ),
        new(
            Key: StageSurface.WidgetKey,
            // Full screen: effects run edge to edge, fall the whole height and flash the whole frame.
            ObsSetup: FullCanvas,
            Name: "Stage",
            Description: "A transparent full-screen layer for one-off effects a command or pipeline triggers — a text or "
                + "emoji running across the screen, confetti, a shake, a flash. Add it once at the full canvas size; "
                + "it draws nothing while idle.",
            DefaultSettings: new() { ["intensity"] = 5, ["accentColor"] = "" },
            DefaultEventSubscriptions: [StageSurface.EventType]
        ),
        new(
            Key: "poll_prediction",
            // One 340 px card (1.45x) with a title and up to five choice bars.
            ObsSetup: Overlay(520, 520),
            Name: "Poll / Prediction",
            Description: "Live poll and prediction bars — choices with vote percentages, lock state, and the winning "
                + "outcome highlighted on resolve — shown only while a round runs.",
            DefaultSettings: new()
            {
                ["colors"] = new Dictionary<string, object>(),
                ["accentColor"] = "",
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
            // One tilted pill up to 600 px wide (1.45x) with an avatar, a title and two sub lines.
            ObsSetup: Overlay(900, 220),
            Name: "Redemption Alert",
            Description: "A channel-point redemption popup — one card at a time with the redeemer, reward, cost, and "
                + "their input — filterable per reward and templatable.",
            DefaultSettings: new()
            {
                ["rewards"] = new List<string>(),
                ["textTemplate"] = "",
                ["durationMs"] = 6000,
                ["soundClipId"] = "",
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: ["reward_redeemed"]
        ),
        new(
            Key: "countdown_timer",
            // One centered card: a label chip over a 52 px HH:MM:SS readout.
            ObsSetup: Overlay(440, 190),
            Name: "Countdown / Timer",
            Description: "A countdown to a wall-clock target or for a fixed duration (BRB / starting soon) with a "
                + "label and completion text — dashboard-controlled live through its widget settings.",
            DefaultSettings: new()
            {
                ["target"] = "",
                ["durationMs"] = 0,
                ["label"] = "Starting soon",
                ["onCompleteText"] = "",
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: []
        ),
        new(
            Key: "emote_wall",
            // Full screen: emotes float up or rain down the whole screen height.
            ObsSetup: FullCanvas,
            Name: "Emote Wall",
            Description: "Emotes from chat float or rain across the screen — harvested from the decorated message "
                + "feed's emote and cheermote fragments, with density, size, and provider filters.",
            DefaultSettings: new()
            {
                ["density"] = 30,
                ["size"] = 48,
                ["animation"] = "float",
                ["providers"] = new List<string>(),
            },
            DefaultEventSubscriptions: ["ChatMessage"]
        ),
        new(
            Key: "custom_data",
            // One small card: a label chip, a 38 px value, and for the gauge a bar with its bounds.
            ObsSetup: Overlay(260, 230),
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
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: ["custom.heartrate"]
        ),
        new(
            Key: "recent_followers",
            // One 300 px card (1.45x) with a heading and the default 5 rows.
            ObsSetup: Overlay(460, 370),
            Name: "Recent Followers",
            Description: "A persistent standings panel of the most recent followers, newest on top — an always-on "
                + "list rather than a one-at-a-time alert, with a configurable count and title.",
            DefaultSettings: new()
            {
                ["count"] = 5,
                ["title"] = "Recent followers",
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: ["follow"]
        ),
        new(
            Key: "sub_train",
            // One pill: the line "Sub train. All aboard." and the count.
            ObsSetup: Overlay(420, 100),
            Name: "Sub Train",
            Description: "A hype counter that spikes on rapid subs and gift subs and cools down on its own — the "
                + "count of subs still inside a rolling window (a gift of N counts as N), hidden when the train stops.",
            DefaultSettings: new() { ["windowMs"] = 300000, ["accentColor"] = "" },
            DefaultEventSubscriptions: ["subscription", "resub", "gift"]
        ),
        new(
            Key: "socials",
            // One tilted pill: a platform chip and a 20 px handle.
            ObsSetup: Overlay(600, 100),
            Name: "Socials",
            Description: "A config-only rotating handles bar — the streamer's social accounts cross-faded through at "
                + "a configurable interval. No event feed; the handles come from the widget settings.",
            DefaultSettings: new()
            {
                ["handles"] = new List<object>(),
                ["rotateMs"] = 8000,
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: []
        ),
        new(
            Key: "top_cheerers",
            // One 300 px card (1.45x) with a heading and the default 5 rows.
            ObsSetup: Overlay(460, 370),
            Name: "Top Cheerers",
            Description: "A ranked board of the session's biggest cheerers by bits — the full leaderboard the labels "
                + "widget only hints at with its single top cheerer, with a configurable depth and title.",
            DefaultSettings: new()
            {
                ["count"] = 5,
                ["title"] = "Top cheerers",
                ["range"] = "stream",
                ["resetAt"] = "",
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: ["cheer"]
        ),
        new(
            Key: "raid_recap",
            // Full screen: a whole scene (credits roll or card grid) whose type sizes follow the 1920 x 1080 frame.
            ObsSetup: FullCanvas,
            Name: "Raid Recap",
            Description: "The stream recap shown as the ending scene when the raid pipeline commits. The widget reads "
                + "the stream's numbers (the metrics setting) when the bot shows it. By default it rolls a credits list "
                + "up the screen and loops; the card grid is the other layout. Sections with no data are left out. "
                + "Holds for a configurable time, then fades.",
            DefaultSettings: new()
            {
                ["layout"] = "credits",
                ["holdSeconds"] = 60,
                ["hueByDay"] = false,
                ["accentColor"] = "",
                ["timeZone"] = "",
                ["rollSeconds"] = 40,
                ["roll"] = true,
                ["showSubs"] = true,
                ["showRaids"] = true,
                ["showChatters"] = true,
                ["showSongs"] = true,
                ["showBits"] = true,
                ["showFollowers"] = true,
                ["showRedemptions"] = true,
                ["showTopCommands"] = true,
                ["metrics"] = new List<string>
                {
                    "duration",
                    "peakViewers",
                    "followers",
                    "newFollowers",
                    "subs",
                    "gifters",
                    "selfSubs",
                    "bits",
                    "raidsIn",
                    "redemptions",
                    "songRequesters",
                    "chat",
                    "topChatters",
                    "commandsRun",
                },
                ["labels"] = new Dictionary<string, object>
                {
                    ["title"] = "Stream recap",
                    ["kpi.duration"] = "Time live",
                    ["kpi.peak"] = "Peak viewers",
                    ["kpi.messages"] = "Chat messages",
                    ["kpi.commands"] = "Commands run",
                    ["block.subs"] = "Subscriptions",
                    ["block.raids"] = "Raids in",
                    ["block.chatters"] = "Top chatters",
                    ["block.songs"] = "Song requests",
                    ["block.bits"] = "Bits",
                    ["block.followers"] = "New followers",
                    ["block.redemptions"] = "Redemptions",
                    ["block.topCommands"] = "Top commands",
                    ["subs.gifted"] = "Gifted",
                    ["subs.own"] = "Subscribed",
                    ["raids.unit.one"] = "raid",
                    ["raids.unit.other"] = "raids",
                    ["viewers.unit.one"] = "viewer",
                    ["viewers.unit.other"] = "viewers",
                    ["chatters.unit.one"] = "chatter",
                    ["chatters.unit.other"] = "chatters",
                    ["songs.unit.one"] = "request",
                    ["songs.unit.other"] = "requests",
                    ["redemptions.unit"] = "redeemed",
                    ["thanks"] = "Thanks for watching! You spent {duration} with us.",
                    ["hours.unit.one"] = "hour",
                    ["hours.unit.other"] = "hours",
                    ["hypeTrain"] = "Hype train reached level {level}.",
                },
            },
            DefaultEventSubscriptions: ["raid_recap.show"]
        ),
        new(
            Key: "lucky_feather",
            // A banner pill stacked over a holder pill, centered at the bottom.
            ObsSetup: Overlay(760, 200),
            Name: "Lucky Feather",
            Description: "Shows who holds the Lucky Feather and announces every steal.",
            DefaultSettings: new()
            {
                ["idleText"] = "The feather is hidden…",
                ["stolenTemplate"] = "",
                ["bannerDurationMs"] = 5000,
                ["accentColor"] = "",
            },
            DefaultEventSubscriptions: []
        ),
    ];

    /// <summary>Every shipped widget. One that draws gets the theme choice; a sound-only one has no look.</summary>
    public static readonly IReadOnlyList<FirstPartyWidgetDefinition> All =
    [
        .. Authored.Select(widget =>
            widget.ObsSetup == SoundOnly || Unthemed.Contains(widget.Key)
                ? widget
                : widget with
                {
                    DefaultSettings = Themed(widget),
                }
        ),
    ];

    /// <summary>
    /// The settings a widget that draws starts with: the default look, the main color of that look in place of an
    /// empty color, and (for a sized overlay, one whose authored settings name an anchor) the distance from the
    /// edge and the scale.
    /// </summary>
    private static Dictionary<string, object> Themed(FirstPartyWidgetDefinition widget)
    {
        Dictionary<string, object> settings = new(widget.DefaultSettings)
        {
            [ThemeSettingKey] = Themes[0],
        };
        if (settings.ContainsKey(AccentSettingKey))
            settings[AccentSettingKey] = ThemeMainColors[Themes[0]];
        if (DefaultAnchors.TryGetValue(widget.Key, out string? anchor))
        {
            settings[AnchorSettingKey] = anchor;
            settings[EdgeOffsetSettingKey] =
                widget.Key == "alerts" ? AlertsDefaultEdgeOffset : DefaultEdgeOffset;
            settings[ScaleSettingKey] = DefaultScale;
        }
        return settings;
    }

    /// <summary>
    /// The entry for a gallery natural key; a retired key resolves to its canonical entry, and a key the catalogue
    /// does not hold (a custom widget, a community submission) gives null.
    /// </summary>
    public static FirstPartyWidgetDefinition? Find(string? naturalKey) =>
        All.FirstOrDefault(w => w.Key == WidgetKeyAliases.Canonical(naturalKey));
}
