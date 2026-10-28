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

using NomNomzBot.Application.Abstractions.Localization;

namespace NomNomzBot.Application.Widgets.Dtos;

/// <summary>
/// The typed settings schema for one first-party widget type (widgets-overlays.md). It is the contract the
/// dashboard renders a generic, schema-driven settings form from — so a first-party widget is configured through
/// controls, never by hand-editing its Vue source. Derived from the widget's authored <c>cfg</c> shape and its
/// catalogue <c>DefaultSettings</c>; every settings key the widget honours is covered by exactly one
/// <see cref="WidgetSettingsField"/> (a coverage test fails the build if a type drifts un-schematised).
/// <see cref="EventSubscriptions"/> is the widget's default wire topics — read-only reference (the widget's data
/// wiring is intrinsic, not user-config), shown so the operator sees what the overlay listens for.
/// </summary>
public sealed record WidgetSettingsSchema(
    string WidgetKey,
    string Name,
    IReadOnlyList<WidgetSettingsField> Fields,
    IReadOnlyList<string> EventSubscriptions
);

/// <summary>
/// One editable setting on a widget. <see cref="Type"/> picks the control the dashboard renders:
/// <c>bool</c> (switch), <c>number</c> (stepper/slider), <c>text</c> (field), <c>color</c> (colour control),
/// <c>select</c> (single-choice dropdown over <see cref="Options"/>), <c>multiselect</c> (chip toggles over
/// <see cref="Options"/>), or <c>json</c> (raw-JSON textarea for a structural map/list the schema does not flatten).
/// <see cref="Default"/> is the catalogue default (so a cleared field falls back to the widget's shipped value);
/// <see cref="Min"/>/<see cref="Max"/>/<see cref="Step"/> bound a numeric control; <see cref="Group"/> lets the form
/// section the fields. <see cref="Picker"/> (a <c>text</c> field only) names a pipeline resource-picker kind
/// (<c>twitch_reward</c>, <c>obs_audio_input</c>, …): the form then offers a searchable list instead of a typed box,
/// and the saved value is that kind's option value. <see cref="Label"/>, <see cref="Help"/>, and <see cref="Group"/> are all
/// <see cref="LocalizedText"/> (S-SCHEMA-I18N-redesign) — a translation KEY only; the dashboard resolves each
/// against <c>strings.xml</c> for the viewer's locale instead of the backend serving a hardcoded English literal.
/// </summary>
public sealed record WidgetSettingsField(
    string Key,
    LocalizedText Label,
    string Type,
    LocalizedText Group,
    object? Default,
    LocalizedText? Help = null,
    IReadOnlyList<WidgetSettingsFieldOption>? Options = null,
    double? Min = null,
    double? Max = null,
    double? Step = null,
    string? Picker = null
);

/// <summary>
/// A single choice for a <c>select</c>/<c>multiselect</c> field: the wire <see cref="Value"/> (the machine value
/// stored/compared, never shown) and its display <see cref="Label"/> — a <see cref="LocalizedText"/>
/// (S-SCHEMA-I18N-redesign) translation KEY only, resolved by the dashboard against <c>strings.xml</c>.
/// </summary>
public sealed record WidgetSettingsFieldOption(string Value, LocalizedText Label);
