// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Authors the typed settings schema for every first-party widget in <see cref="FirstPartyWidgetCatalogue"/>. Each
/// field's control type + options are hand-authored from the widget's Vue <c>cfg</c> shape, while its default value
/// is read back from the catalogue's <c>DefaultSettings</c> — so a default can never drift from what the seeder
/// ships. A structural map/list a form can't flatten (goal colours, socials handles, redemption reward filter) is
/// exposed as a <c>json</c> field (raw-JSON textarea) so every settings key stays covered; <see cref="WidgetSettingsSchemaTests"/>
/// fails the build if a type or a key is left un-schematised.
/// </summary>
public sealed class WidgetSettingsSchemaProvider : IWidgetSettingsSchemaProvider
{
    // Control types the dashboard's generic form renders (kept in sync with the frontend field renderer). The
    // bool/text literals live inline in their factories; the rest are named here where they are reused.
    private const string Number = "number";
    private const string Color = "color";
    private const string Select = "select";
    private const string Multiselect = "multiselect";
    private const string Json = "json";

    // Field groups the form sections by.
    private const string Content = "Content";
    private const string Appearance = "Appearance";
    private const string Behaviour = "Behaviour";
    private const string Data = "Data";

    private static readonly IReadOnlyList<WidgetSettingsFieldOption> EventOptions =
    [
        new("follow", "Follow"),
        new("subscription", "Subscription"),
        new("resub", "Resub"),
        new("gift", "Gift sub"),
        new("cheer", "Cheer"),
        new("raid", "Raid"),
        new("supporter.tip", "Tip"),
        new("supporter.membership", "Membership"),
        new("supporter.merch", "Merch"),
        new("supporter.charity", "Charity"),
    ];

    private static readonly IReadOnlyList<WidgetSettingsFieldOption> ProviderOptions =
    [
        new("twitch", "Twitch"),
        new("bttv", "BetterTTV"),
        new("ffz", "FrankerFaceZ"),
        new("7tv", "7TV"),
    ];

    private readonly IReadOnlyList<WidgetSettingsSchema> _all;
    private readonly Dictionary<string, WidgetSettingsSchema> _byKey;

    public WidgetSettingsSchemaProvider()
    {
        _all =
        [
            .. FirstPartyWidgetCatalogue.All.Select(definition => new WidgetSettingsSchema(
                definition.Key,
                definition.Name,
                FieldsFor(definition),
                definition.DefaultEventSubscriptions
            )),
        ];
        _byKey = _all.ToDictionary(schema => schema.WidgetKey, StringComparer.Ordinal);
    }

    public IReadOnlyList<WidgetSettingsSchema> GetAll() => _all;

    public WidgetSettingsSchema? GetByKey(string widgetKey) =>
        _byKey.TryGetValue(widgetKey, out WidgetSettingsSchema? schema) ? schema : null;

    // The authored field list per widget key. Every DefaultSettings key of the definition MUST appear exactly once
    // (enforced by WidgetSettingsSchemaTests). The accent colour is the last field on every widget that has one.
    private static IReadOnlyList<WidgetSettingsField> FieldsFor(FirstPartyWidgetDefinition d) =>
        d.Key switch
        {
            "alerts" =>
            [
                Multi(d, "events", "Alert types", Content, EventOptions),
                Text(
                    d,
                    "textTemplate",
                    "Text template",
                    Content,
                    "Optional override; blank uses the built-in copy."
                ),
                NumberField(d, "durationMs", "On-screen time (ms)", Behaviour, min: 0, step: 100),
                NumberField(d, "minBits", "Minimum bits", Behaviour, min: 0),
                NumberField(d, "minGiftCount", "Minimum gift count", Behaviour, min: 0),
                NumberField(d, "minAmount", "Minimum supporter amount", Behaviour, min: 0),
                Accent(d),
            ],
            "goal_bar" =>
            [
                SelectField(
                    d,
                    "metric",
                    "Metric",
                    Content,
                    Opts(("followers", "Followers"), ("subs", "Subscribers"), ("bits", "Bits"))
                ),
                NumberField(d, "target", "Target", Content, min: 0),
                NumberField(d, "start", "Starting value", Content, min: 0),
                Text(
                    d,
                    "resetCadence",
                    "Reset cadence label",
                    Content,
                    "Shown beside the bar, e.g. \"this month\"."
                ),
                JsonField(
                    d,
                    "colors",
                    "Colours",
                    Appearance,
                    "Optional colour overrides as a JSON object."
                ),
                JsonField(
                    d,
                    "labels",
                    "Labels",
                    Appearance,
                    "Optional label overrides (e.g. a custom title) as a JSON object."
                ),
            ],
            "labels" =>
            [
                SelectField(
                    d,
                    "label",
                    "Stat",
                    Content,
                    Opts(
                        ("latest_follower", "Latest follower"),
                        ("follower_count", "Follower count"),
                        ("latest_sub", "Latest subscriber"),
                        ("sub_count", "Subscriber count"),
                        ("top_cheerer", "Top cheerer")
                    )
                ),
                Text(
                    d,
                    "formatString",
                    "Format string",
                    Content,
                    "Optional; use {value} as the placeholder."
                ),
                Accent(d),
            ],
            "drop_game" or "raffle" or "heist" or "crash" =>
            [
                NumberField(d, "hideAfterMs", "Hide after (ms)", Behaviour, min: 0, step: 100),
                Accent(d),
            ],
            "event_ticker" =>
            [
                Multi(d, "events", "Event types", Content, EventOptions),
                NumberField(d, "speed", "Scroll speed", Behaviour, min: 0),
                NumberField(d, "count", "Items kept", Behaviour, min: 1, max: 50, step: 1),
                Accent(d),
            ],
            "chat_box" =>
            [
                SelectField(
                    d,
                    "theme",
                    "Theme",
                    Appearance,
                    Opts(("dark", "Dark"), ("light", "Light"), ("transparent", "Transparent"))
                ),
                Text(d, "fontFamily", "Font family", Appearance, "Blank uses the overlay default."),
                NumberField(d, "fontSize", "Font size", Appearance, min: 8, max: 48, step: 1),
                ColorField(
                    d,
                    "background",
                    "Background",
                    Appearance,
                    "Blank uses the theme background."
                ),
                NumberField(
                    d,
                    "backgroundOpacity",
                    "Background opacity",
                    Appearance,
                    min: 0,
                    max: 1,
                    step: 0.01
                ),
                Bool(d, "showTimestamps", "Show timestamps", Content),
                NumberField(d, "maxMessages", "Max messages", Behaviour, min: 1),
                NumberField(
                    d,
                    "fadeAfterMs",
                    "Fade after (ms, 0 = never)",
                    Behaviour,
                    min: 0,
                    step: 100
                ),
                Bool(d, "showBadges", "Show badges", Content),
                Bool(d, "showEmotes", "Show emotes", Content),
                Bool(d, "hideCommands", "Hide command messages", Content),
                Bool(d, "hideBots", "Hide bot messages", Content),
                Accent(d),
            ],
            "now_playing" =>
            [
                SelectField(
                    d,
                    "layout",
                    "Layout",
                    Appearance,
                    Opts(("pill", "Pill"), ("card", "Card"))
                ),
                Bool(d, "showArt", "Show album art", Content),
                Bool(d, "showProgressBar", "Show progress bar", Content),
                Text(d, "provider", "Provider filter", Content, "Blank shows any; e.g. spotify."),
                Accent(d),
            ],
            "sr_queue" =>
            [
                NumberField(d, "count", "Items shown", Content, min: 1, max: 50, step: 1),
                Bool(d, "showRequester", "Show requester", Content),
                Bool(d, "showDuration", "Show duration", Content),
                Accent(d),
            ],
            "tts_caption" =>
            [
                Bool(d, "showText", "Show caption text", Content),
                Bool(d, "voiceLabel", "Show voice label", Content),
                SelectField(
                    d,
                    "position",
                    "Position",
                    Appearance,
                    Opts(("top", "Top"), ("bottom", "Bottom"))
                ),
                Accent(d),
            ],
            "poll_prediction" =>
            [
                SelectField(
                    d,
                    "position",
                    "Position",
                    Appearance,
                    Opts(("left", "Left"), ("right", "Right"))
                ),
                JsonField(
                    d,
                    "colors",
                    "Colours",
                    Appearance,
                    "Optional per-outcome colour overrides as a JSON object."
                ),
                Accent(d),
            ],
            "redemption_alert" =>
            [
                JsonField(
                    d,
                    "rewards",
                    "Reward filter",
                    Content,
                    "Optional list of reward ids to show (JSON array); blank shows all."
                ),
                Text(
                    d,
                    "textTemplate",
                    "Text template",
                    Content,
                    "Optional override for the popup copy."
                ),
                Text(d, "sound", "Sound", Content, "Optional sound clip name to play."),
                NumberField(d, "durationMs", "On-screen time (ms)", Behaviour, min: 0, step: 100),
                Accent(d),
            ],
            "countdown_timer" =>
            [
                Text(
                    d,
                    "target",
                    "Target time",
                    Content,
                    "ISO date-time to count down to; blank uses the duration."
                ),
                NumberField(d, "durationMs", "Duration (ms)", Content, min: 0, step: 1000),
                Text(d, "label", "Label", Content),
                Text(d, "onCompleteText", "On-complete text", Content),
                Accent(d),
            ],
            "emote_wall" =>
            [
                NumberField(d, "density", "Density", Behaviour, min: 1, max: 100, step: 1),
                NumberField(d, "size", "Emote size (px)", Appearance, min: 8, max: 128, step: 1),
                SelectField(
                    d,
                    "animation",
                    "Animation",
                    Appearance,
                    Opts(("float", "Float up"), ("rain", "Rain down"))
                ),
                Multi(d, "providers", "Emote providers", Content, ProviderOptions),
                Accent(d),
            ],
            "custom_data" =>
            [
                Text(d, "source", "Source", Data, "The custom data source key, e.g. heartrate."),
                Text(d, "field", "Field", Data, "The field within the source, e.g. bpm."),
                SelectField(
                    d,
                    "render",
                    "Render as",
                    Appearance,
                    Opts(("number", "Number"), ("gauge", "Gauge"), ("text", "Text"))
                ),
                Text(d, "label", "Label", Content),
                NumberField(d, "min", "Gauge minimum", Data),
                NumberField(d, "max", "Gauge maximum", Data),
                Accent(d),
            ],
            "recent_followers" =>
            [
                NumberField(d, "count", "Followers shown", Content, min: 1, max: 50, step: 1),
                Text(d, "title", "Title", Content),
                Accent(d),
            ],
            "sub_train" =>
            [
                NumberField(d, "windowMs", "Window (ms)", Behaviour, min: 0, step: 1000),
                Accent(d),
            ],
            "socials" =>
            [
                JsonField(
                    d,
                    "handles",
                    "Handles",
                    Content,
                    "The social accounts to rotate, as a JSON array of objects (e.g. label + url)."
                ),
                NumberField(d, "rotateMs", "Rotate interval (ms)", Behaviour, min: 0, step: 500),
                Accent(d),
            ],
            "top_cheerers" =>
            [
                NumberField(d, "count", "Cheerers shown", Content, min: 1, max: 50, step: 1),
                Text(d, "title", "Title", Content),
                Accent(d),
            ],
            _ => throw new InvalidOperationException(
                $"No settings schema authored for first-party widget '{d.Key}'."
            ),
        };

    // ── Field factories (default value is always read back from the catalogue) ──────────────────────────────────

    private static object? DefaultOf(FirstPartyWidgetDefinition d, string key) =>
        d.DefaultSettings.TryGetValue(key, out object? value) ? value : null;

    private static WidgetSettingsField Bool(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group
    ) => new(key, label, "bool", group, DefaultOf(d, key));

    private static WidgetSettingsField NumberField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        double? min = null,
        double? max = null,
        double? step = null,
        string? help = null
    ) => new(key, label, Number, group, DefaultOf(d, key), help, null, min, max, step);

    private static WidgetSettingsField Text(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        string? help = null
    ) => new(key, label, "text", group, DefaultOf(d, key), help);

    private static WidgetSettingsField ColorField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        string? help = null
    ) => new(key, label, Color, group, DefaultOf(d, key), help);

    private static WidgetSettingsField Accent(FirstPartyWidgetDefinition d) =>
        new("accentColor", "Accent colour", Color, Appearance, DefaultOf(d, "accentColor"));

    private static WidgetSettingsField SelectField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        IReadOnlyList<WidgetSettingsFieldOption> options
    ) => new(key, label, Select, group, DefaultOf(d, key), null, options);

    private static WidgetSettingsField Multi(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        IReadOnlyList<WidgetSettingsFieldOption> options
    ) => new(key, label, Multiselect, group, DefaultOf(d, key), null, options);

    private static WidgetSettingsField JsonField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        string? help = null
    ) => new(key, label, Json, group, DefaultOf(d, key), help);

    private static IReadOnlyList<WidgetSettingsFieldOption> Opts(
        params (string Value, string Label)[] options
    ) => [.. options.Select(option => new WidgetSettingsFieldOption(option.Value, option.Label))];
}
