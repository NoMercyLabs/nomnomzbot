// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Infrastructure.Content.Widgets;

/// <summary>
/// Authors the typed settings schema for every first-party widget in <see cref="FirstPartyWidgetCatalogue"/>. Each
/// field's control type + options are hand-authored from the widget's Vue <c>cfg</c> shape, while its default value
/// is read back from the catalogue's <c>DefaultSettings</c> — so a default can never drift from what the seeder
/// ships. A structural map/list a form can't flatten (goal colours, socials handles, redemption reward filter) is
/// exposed as a <c>json</c> field (raw-JSON textarea) so every settings key stays covered; <see cref="WidgetSettingsSchemaTests"/>
/// fails the build if a type or a key is left un-schematised. Every field's <c>Label</c>/<c>Help</c> is a
/// <see cref="LocalizedText"/> (S-SCHEMA-I18N) — the English literal passed to each field factory below is looked
/// up in <see cref="NlTranslations"/> for its Dutch counterpart; a missing entry resolves to an empty Dutch string,
/// which <c>WidgetSettingsSchemaI18nTests</c> fails the build on (fail loud, never a silent English fallback).
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

    // Dutch translations for every field Label/Help authored below, keyed "{widgetKey}.{fieldKey}.{label|help}".
    // Looked up by Loc(); WidgetSettingsSchemaI18nTests enumerates the REAL schema (Provider.GetAll()) and fails
    // the build if any field's resolved Nl is blank, so this table cannot silently fall behind the English copy.
    private static readonly IReadOnlyDictionary<string, string> NlTranslations = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["alerts.events.label"] = "Waarschuwingstypen",
        ["alerts.textTemplate.label"] = "Tekstsjabloon",
        ["alerts.textTemplate.help"] =
            "Optionele overschrijving; leeg gebruikt de ingebouwde tekst.",
        ["alerts.durationMs.label"] = "Weergavetijd (ms)",
        ["alerts.minBits.label"] = "Minimum bits",
        ["alerts.minGiftCount.label"] = "Minimum aantal gift-subs",
        ["alerts.minAmount.label"] = "Minimumbedrag van supporter",
        ["goal_bar.metric.label"] = "Metriek",
        ["goal_bar.target.label"] = "Doel",
        ["goal_bar.start.label"] = "Startwaarde",
        ["goal_bar.resetCadence.label"] = "Label voor resetinterval",
        ["goal_bar.resetCadence.help"] = "Wordt naast de balk getoond, bijv. \"deze maand\".",
        ["goal_bar.colors.label"] = "Kleuren",
        ["goal_bar.colors.help"] = "Optionele kleuroverschrijvingen als JSON-object.",
        ["goal_bar.labels.label"] = "Labels",
        ["goal_bar.labels.help"] =
            "Optionele labeloverschrijvingen (bijv. een eigen titel) als JSON-object.",
        ["labels.label.label"] = "Statistiek",
        ["labels.formatString.label"] = "Opmaakstring",
        ["labels.formatString.help"] = "Optioneel; gebruik {value} als plaatshouder.",
        ["drop_game.hideAfterMs.label"] = "Verbergen na (ms)",
        ["raffle.hideAfterMs.label"] = "Verbergen na (ms)",
        ["heist.hideAfterMs.label"] = "Verbergen na (ms)",
        ["crash.hideAfterMs.label"] = "Verbergen na (ms)",
        ["event_ticker.events.label"] = "Gebeurtenistypen",
        ["event_ticker.speed.label"] = "Scrollsnelheid",
        ["event_ticker.count.label"] = "Aantal bewaarde items",
        ["chat_box.theme.label"] = "Thema",
        ["chat_box.fontFamily.label"] = "Lettertype",
        ["chat_box.fontFamily.help"] = "Leeg gebruikt de standaard van de overlay.",
        ["chat_box.fontSize.label"] = "Lettergrootte",
        ["chat_box.background.label"] = "Achtergrond",
        ["chat_box.background.help"] = "Leeg gebruikt de achtergrond van het thema.",
        ["chat_box.backgroundOpacity.label"] = "Achtergrondtransparantie",
        ["chat_box.showTimestamps.label"] = "Tijdstempels tonen",
        ["chat_box.maxMessages.label"] = "Max. aantal berichten",
        ["chat_box.fadeAfterMs.label"] = "Vervagen na (ms, 0 = nooit)",
        ["chat_box.showBadges.label"] = "Badges tonen",
        ["chat_box.showEmotes.label"] = "Emotes tonen",
        ["chat_box.hideCommands.label"] = "Commandoberichten verbergen",
        ["chat_box.hideBots.label"] = "Botberichten verbergen",
        ["now_playing.layout.label"] = "Lay-out",
        ["now_playing.showArt.label"] = "Albumhoes tonen",
        ["now_playing.showProgressBar.label"] = "Voortgangsbalk tonen",
        ["now_playing.provider.label"] = "Providerfilter",
        ["now_playing.provider.help"] = "Leeg toont alle; bijv. spotify.",
        ["now_playing.enableAudio.label"] =
            "Spotify-audioapparaat (uitschakelen zodat deze widget niet langer als Spotify Connect-apparaat fungeert)",
        ["now_playing.youtubeMode.label"] = "YouTube-tracks weergeven als",
        ["sr_queue.count.label"] = "Aantal getoonde items",
        ["sr_queue.showRequester.label"] = "Aanvrager tonen",
        ["sr_queue.showDuration.label"] = "Duur tonen",
        ["tts_audio.showIndicator.label"] = "Spreekindicator tonen (alleen instellen)",
        ["tts_caption.showText.label"] = "Ondertiteltekst tonen",
        ["tts_caption.voiceLabel.label"] = "Stemlabel tonen",
        ["tts_caption.position.label"] = "Positie",
        ["poll_prediction.position.label"] = "Positie",
        ["poll_prediction.colors.label"] = "Kleuren",
        ["poll_prediction.colors.help"] =
            "Optionele kleuroverschrijvingen per uitkomst als JSON-object.",
        ["redemption_alert.rewards.label"] = "Beloningsfilter",
        ["redemption_alert.rewards.help"] =
            "Optionele lijst met beloning-id's om te tonen (JSON-array); leeg toont alles.",
        ["redemption_alert.textTemplate.label"] = "Tekstsjabloon",
        ["redemption_alert.textTemplate.help"] = "Optionele overschrijving voor de pop-uptekst.",
        ["redemption_alert.durationMs.label"] = "Weergavetijd (ms)",
        ["redemption_alert.soundClipId.label"] = "Waarschuwingsgeluid",
        ["redemption_alert.soundClipId.help"] =
            "Speelt dit fragment uit je Sound Clips-bibliotheek af telkens wanneer de waarschuwing afgaat "
            + "— voer de id of naam van het fragment in. Laat leeg en de waarschuwing blijft stil.",
        ["countdown_timer.target.label"] = "Doeltijd",
        ["countdown_timer.target.help"] =
            "ISO-datumtijd om naartoe af te tellen; leeg gebruikt de duur.",
        ["countdown_timer.durationMs.label"] = "Duur (ms)",
        ["countdown_timer.label.label"] = "Label",
        ["countdown_timer.onCompleteText.label"] = "Tekst bij voltooiing",
        ["emote_wall.density.label"] = "Dichtheid",
        ["emote_wall.size.label"] = "Emote-grootte (px)",
        ["emote_wall.animation.label"] = "Animatie",
        ["emote_wall.providers.label"] = "Emote-providers",
        ["custom_data.source.label"] = "Bron",
        ["custom_data.source.help"] = "De sleutel van de aangepaste databron, bijv. heartrate.",
        ["custom_data.field.label"] = "Veld",
        ["custom_data.field.help"] = "Het veld binnen de bron, bijv. bpm.",
        ["custom_data.render.label"] = "Weergeven als",
        ["custom_data.label.label"] = "Label",
        ["custom_data.min.label"] = "Minimum van de meter",
        ["custom_data.max.label"] = "Maximum van de meter",
        ["recent_followers.count.label"] = "Aantal getoonde volgers",
        ["recent_followers.title.label"] = "Titel",
        ["sub_train.windowMs.label"] = "Venster (ms)",
        ["socials.handles.label"] = "Accounts",
        ["socials.handles.help"] =
            "De social-accounts om te laten rouleren, als JSON-array van { label, handle } objecten — bijv. "
            + """[{"label":"Twitter","handle":"@you"}]. Een item met een lege handle wordt genegeerd.""",
        ["socials.rotateMs.label"] = "Rotatie-interval (ms)",
        ["top_cheerers.count.label"] = "Aantal getoonde cheerers",
        ["top_cheerers.title.label"] = "Titel",
    };

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
                Bool(
                    d,
                    "enableAudio",
                    "Spotify audio device (turn off to stop this widget acting as a Spotify Connect device)",
                    Behaviour
                ),
                SelectField(
                    d,
                    "youtubeMode",
                    "YouTube tracks render as",
                    Content,
                    Opts(("card", "Compact card"), ("video", "Video"))
                ),
                Accent(d),
            ],
            "sr_queue" =>
            [
                NumberField(d, "count", "Items shown", Content, min: 1, max: 50, step: 1),
                Bool(d, "showRequester", "Show requester", Content),
                Bool(d, "showDuration", "Show duration", Content),
                Accent(d),
            ],
            "tts_audio" =>
            [
                Bool(d, "showIndicator", "Show a speaking dot (setup only)", Content),
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
                NumberField(d, "durationMs", "On-screen time (ms)", Behaviour, min: 0, step: 100),
                Text(
                    d,
                    "soundClipId",
                    "Alert sound",
                    Behaviour,
                    "Plays this clip from your Sound Clips library every time the alert fires — enter the "
                        + "clip's id or name. Leave blank and the alert stays silent."
                ),
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
                    "The social accounts to rotate, as a JSON array of { label, handle } objects — e.g. "
                        + """[{"label":"Twitter","handle":"@you"}]. An entry with a blank handle is dropped."""
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

    // Resolves the English literal authored inline at each call site to a LocalizedText carrying its Dutch
    // counterpart from NlTranslations. A missing table entry resolves to an empty Dutch string rather than
    // throwing here — WidgetSettingsSchemaI18nTests is the single place that fails the build on it, so every
    // gap surfaces as one readable test failure instead of an opaque constructor-time crash.
    private static LocalizedText Loc(
        FirstPartyWidgetDefinition d,
        string fieldKey,
        string suffix,
        string en
    )
    {
        string translationKey = $"{d.Key}.{fieldKey}.{suffix}";
        string nl = NlTranslations.TryGetValue(translationKey, out string? value)
            ? value
            : string.Empty;
        return new LocalizedText($"widget.{translationKey}", en, nl);
    }

    private static WidgetSettingsField Bool(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group
    ) => new(key, Loc(d, key, "label", label), "bool", group, DefaultOf(d, key));

    private static WidgetSettingsField NumberField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        double? min = null,
        double? max = null,
        double? step = null,
        string? help = null
    ) =>
        new(
            key,
            Loc(d, key, "label", label),
            Number,
            group,
            DefaultOf(d, key),
            help is null ? null : Loc(d, key, "help", help),
            null,
            min,
            max,
            step
        );

    private static WidgetSettingsField Text(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        string? help = null
    ) =>
        new(
            key,
            Loc(d, key, "label", label),
            "text",
            group,
            DefaultOf(d, key),
            help is null ? null : Loc(d, key, "help", help)
        );

    private static WidgetSettingsField ColorField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        string? help = null
    ) =>
        new(
            key,
            Loc(d, key, "label", label),
            Color,
            group,
            DefaultOf(d, key),
            help is null ? null : Loc(d, key, "help", help)
        );

    // The accent colour field is identical (key/label/no-help) on every widget that has one, so it is translated
    // directly rather than through NlTranslations.
    private static WidgetSettingsField Accent(FirstPartyWidgetDefinition d) =>
        new(
            "accentColor",
            new LocalizedText($"widget.{d.Key}.accentColor.label", "Accent colour", "Accentkleur"),
            Color,
            Appearance,
            DefaultOf(d, "accentColor")
        );

    private static WidgetSettingsField SelectField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        IReadOnlyList<WidgetSettingsFieldOption> options
    ) => new(key, Loc(d, key, "label", label), Select, group, DefaultOf(d, key), null, options);

    private static WidgetSettingsField Multi(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        IReadOnlyList<WidgetSettingsFieldOption> options
    ) =>
        new(key, Loc(d, key, "label", label), Multiselect, group, DefaultOf(d, key), null, options);

    private static WidgetSettingsField JsonField(
        FirstPartyWidgetDefinition d,
        string key,
        string label,
        string group,
        string? help = null
    ) =>
        new(
            key,
            Loc(d, key, "label", label),
            Json,
            group,
            DefaultOf(d, key),
            help is null ? null : Loc(d, key, "help", help)
        );

    private static IReadOnlyList<WidgetSettingsFieldOption> Opts(
        params (string Value, string Label)[] options
    ) => [.. options.Select(option => new WidgetSettingsFieldOption(option.Value, option.Label))];
}
