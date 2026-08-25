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
/// <see cref="LocalizedText"/> KEY ONLY (S-SCHEMA-I18N-redesign) — <see cref="LabelKey"/>/<see cref="HelpKey"/>
/// derive it deterministically from <c>widget.{widgetKey}.{fieldKey}.{label|help}</c>; the English and Dutch text
/// itself lives in <c>strings.xml</c>/<c>values-nl/strings.xml</c>, never inline in this file. A committed key
/// manifest (<c>SchemaLocalizationManifestTests</c>) sweeps the REAL schema this class produces and fails the
/// build if a key drifts from what the dashboard's translation files declare.
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

    // Field groups the form sections by — each a LocalizedText KEY (S-SCHEMA-I18N-redesign); the display text
    // lives in strings.xml/values-nl/strings.xml under the same widget.group.* namespace.
    private static readonly LocalizedText Content = new("widget.group.content");
    private static readonly LocalizedText Appearance = new("widget.group.appearance");
    private static readonly LocalizedText Behaviour = new("widget.group.behaviour");
    private static readonly LocalizedText Data = new("widget.group.data");

    // Shared across every widget that offers this choice, so the option's meaning (and its key) never drifts
    // between e.g. "alerts" and "event_ticker": widget.option.{namespace}.{value}.
    private static readonly IReadOnlyList<WidgetSettingsFieldOption> EventOptions =
    [
        SharedOption("events", "follow"),
        SharedOption("events", "subscription"),
        SharedOption("events", "resub"),
        SharedOption("events", "gift"),
        SharedOption("events", "cheer"),
        SharedOption("events", "raid"),
        SharedOption("events", "supporter.tip"),
        SharedOption("events", "supporter.membership"),
        SharedOption("events", "supporter.merch"),
        SharedOption("events", "supporter.charity"),
    ];

    private static readonly IReadOnlyList<WidgetSettingsFieldOption> ProviderOptions =
    [
        SharedOption("providers", "twitch"),
        SharedOption("providers", "bttv"),
        SharedOption("providers", "ffz"),
        SharedOption("providers", "7tv"),
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
                Multi(d, "events", Content, EventOptions),
                Text(d, "textTemplate", Content, help: true),
                NumberField(d, "durationMs", Behaviour, min: 0, step: 100),
                NumberField(d, "minBits", Behaviour, min: 0),
                NumberField(d, "minGiftCount", Behaviour, min: 0),
                NumberField(d, "minAmount", Behaviour, min: 0),
                Accent(d),
            ],
            "goal_bar" =>
            [
                SelectField(d, "metric", Content, Opts(d, "metric", "followers", "subs", "bits")),
                NumberField(d, "target", Content, min: 0),
                NumberField(d, "start", Content, min: 0),
                Text(d, "resetCadence", Content, help: true),
                JsonField(d, "colors", Appearance, help: true),
                JsonField(d, "labels", Appearance, help: true),
            ],
            "labels" =>
            [
                SelectField(
                    d,
                    "label",
                    Content,
                    Opts(
                        d,
                        "label",
                        "latest_follower",
                        "follower_count",
                        "latest_sub",
                        "sub_count",
                        "top_cheerer"
                    )
                ),
                Text(d, "formatString", Content, help: true),
                Accent(d),
            ],
            "drop_game" or "raffle" or "heist" or "crash" =>
            [
                NumberField(d, "hideAfterMs", Behaviour, min: 0, step: 100),
                Accent(d),
            ],
            "event_ticker" =>
            [
                Multi(d, "events", Content, EventOptions),
                NumberField(d, "speed", Behaviour, min: 0),
                NumberField(d, "count", Behaviour, min: 1, max: 50, step: 1),
                Accent(d),
            ],
            "chat_box" =>
            [
                SelectField(
                    d,
                    "theme",
                    Appearance,
                    Opts(d, "theme", "dark", "light", "transparent")
                ),
                Text(d, "fontFamily", Appearance, help: true),
                NumberField(d, "fontSize", Appearance, min: 8, max: 48, step: 1),
                ColorField(d, "background", Appearance, help: true),
                NumberField(d, "backgroundOpacity", Appearance, min: 0, max: 1, step: 0.01),
                Bool(d, "showTimestamps", Content),
                NumberField(d, "maxMessages", Behaviour, min: 1),
                NumberField(d, "fadeAfterMs", Behaviour, min: 0, step: 100),
                Bool(d, "showBadges", Content),
                Bool(d, "showEmotes", Content),
                Bool(d, "hideCommands", Content),
                Bool(d, "hideBots", Content),
                Accent(d),
            ],
            "now_playing" =>
            [
                SelectField(d, "layout", Appearance, Opts(d, "layout", "pill", "card")),
                Bool(d, "showArt", Content),
                Bool(d, "showProgressBar", Content),
                Text(d, "provider", Content, help: true),
                Bool(d, "enableAudio", Behaviour),
                SelectField(d, "youtubeMode", Content, Opts(d, "youtubeMode", "card", "video")),
                Accent(d),
            ],
            "sr_queue" =>
            [
                NumberField(d, "count", Content, min: 1, max: 50, step: 1),
                Bool(d, "showRequester", Content),
                Bool(d, "showDuration", Content),
                Accent(d),
            ],
            "tts_audio" => [Bool(d, "showIndicator", Content), Accent(d)],
            "tts_caption" =>
            [
                Bool(d, "showText", Content),
                Bool(d, "voiceLabel", Content),
                SelectField(d, "position", Appearance, Opts(d, "position", "top", "bottom")),
                Accent(d),
            ],
            "poll_prediction" =>
            [
                SelectField(d, "position", Appearance, Opts(d, "position", "left", "right")),
                JsonField(d, "colors", Appearance, help: true),
                Accent(d),
            ],
            "redemption_alert" =>
            [
                JsonField(d, "rewards", Content, help: true),
                Text(d, "textTemplate", Content, help: true),
                NumberField(d, "durationMs", Behaviour, min: 0, step: 100),
                Text(d, "soundClipId", Behaviour, help: true),
                Accent(d),
            ],
            "countdown_timer" =>
            [
                Text(d, "target", Content, help: true),
                NumberField(d, "durationMs", Content, min: 0, step: 1000),
                Text(d, "label", Content),
                Text(d, "onCompleteText", Content),
                Accent(d),
            ],
            "emote_wall" =>
            [
                NumberField(d, "density", Behaviour, min: 1, max: 100, step: 1),
                NumberField(d, "size", Appearance, min: 8, max: 128, step: 1),
                SelectField(d, "animation", Appearance, Opts(d, "animation", "float", "rain")),
                Multi(d, "providers", Content, ProviderOptions),
                Accent(d),
            ],
            "custom_data" =>
            [
                Text(d, "source", Data, help: true),
                Text(d, "field", Data, help: true),
                SelectField(d, "render", Appearance, Opts(d, "render", "number", "gauge", "text")),
                Text(d, "label", Content),
                NumberField(d, "min", Data),
                NumberField(d, "max", Data),
                Accent(d),
            ],
            "recent_followers" =>
            [
                NumberField(d, "count", Content, min: 1, max: 50, step: 1),
                Text(d, "title", Content),
                Accent(d),
            ],
            "sub_train" => [NumberField(d, "windowMs", Behaviour, min: 0, step: 1000), Accent(d)],
            "socials" =>
            [
                JsonField(d, "handles", Content, help: true),
                NumberField(d, "rotateMs", Behaviour, min: 0, step: 500),
                Accent(d),
            ],
            "top_cheerers" =>
            [
                NumberField(d, "count", Content, min: 1, max: 50, step: 1),
                Text(d, "title", Content),
                Accent(d),
            ],
            _ => throw new InvalidOperationException(
                $"No settings schema authored for first-party widget '{d.Key}'."
            ),
        };

    // ── Field factories (default value is always read back from the catalogue) ──────────────────────────────────

    private static object? DefaultOf(FirstPartyWidgetDefinition d, string key) =>
        d.DefaultSettings.TryGetValue(key, out object? value) ? value : null;

    // Deterministic translation-key convention (S-SCHEMA-I18N-redesign): `widget.{widgetKey}.{fieldKey}.label` /
    // `.help`. No English/Dutch text is ever authored here — SchemaLocalizationManifestTests sweeps the REAL
    // schema this class produces and fails the build if a key it emits has no matching entry in strings.xml /
    // values-nl/strings.xml.
    private static LocalizedText LabelKey(FirstPartyWidgetDefinition d, string fieldKey) =>
        new($"widget.{d.Key}.{fieldKey}.label");

    private static LocalizedText HelpKey(FirstPartyWidgetDefinition d, string fieldKey) =>
        new($"widget.{d.Key}.{fieldKey}.help");

    private static WidgetSettingsField Bool(
        FirstPartyWidgetDefinition d,
        string key,
        LocalizedText group
    ) => new(key, LabelKey(d, key), "bool", group, DefaultOf(d, key));

    private static WidgetSettingsField NumberField(
        FirstPartyWidgetDefinition d,
        string key,
        LocalizedText group,
        double? min = null,
        double? max = null,
        double? step = null,
        bool help = false
    ) =>
        new(
            key,
            LabelKey(d, key),
            Number,
            group,
            DefaultOf(d, key),
            help ? HelpKey(d, key) : null,
            null,
            min,
            max,
            step
        );

    private static WidgetSettingsField Text(
        FirstPartyWidgetDefinition d,
        string key,
        LocalizedText group,
        bool help = false
    ) =>
        new(key, LabelKey(d, key), "text", group, DefaultOf(d, key), help ? HelpKey(d, key) : null);

    private static WidgetSettingsField ColorField(
        FirstPartyWidgetDefinition d,
        string key,
        LocalizedText group,
        bool help = false
    ) => new(key, LabelKey(d, key), Color, group, DefaultOf(d, key), help ? HelpKey(d, key) : null);

    // The accent colour field is identical (key/no-help) on every widget that has one, but still gets its own
    // per-widget translation key (widget.{widgetKey}.accentColor.label) so a future widget can override the copy.
    private static WidgetSettingsField Accent(FirstPartyWidgetDefinition d) =>
        new(
            "accentColor",
            LabelKey(d, "accentColor"),
            Color,
            Appearance,
            DefaultOf(d, "accentColor")
        );

    private static WidgetSettingsField SelectField(
        FirstPartyWidgetDefinition d,
        string key,
        LocalizedText group,
        IReadOnlyList<WidgetSettingsFieldOption> options
    ) => new(key, LabelKey(d, key), Select, group, DefaultOf(d, key), null, options);

    private static WidgetSettingsField Multi(
        FirstPartyWidgetDefinition d,
        string key,
        LocalizedText group,
        IReadOnlyList<WidgetSettingsFieldOption> options
    ) => new(key, LabelKey(d, key), Multiselect, group, DefaultOf(d, key), null, options);

    private static WidgetSettingsField JsonField(
        FirstPartyWidgetDefinition d,
        string key,
        LocalizedText group,
        bool help = false
    ) => new(key, LabelKey(d, key), Json, group, DefaultOf(d, key), help ? HelpKey(d, key) : null);

    private static IReadOnlyList<WidgetSettingsFieldOption> Opts(
        FirstPartyWidgetDefinition d,
        string fieldKey,
        params string[] values
    ) => [.. values.Select(value => Option(d, fieldKey, value))];

    private static WidgetSettingsFieldOption Option(
        FirstPartyWidgetDefinition d,
        string fieldKey,
        string value
    ) => new(value, new($"widget.{d.Key}.{fieldKey}.option.{value}"));

    private static WidgetSettingsFieldOption SharedOption(string @namespace, string value) =>
        new(value, new($"widget.option.{@namespace}.{value}"));
}
