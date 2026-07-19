// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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
/// section the fields.
/// </summary>
public sealed record WidgetSettingsField(
    string Key,
    string Label,
    string Type,
    string Group,
    object? Default,
    string? Help = null,
    IReadOnlyList<WidgetSettingsFieldOption>? Options = null,
    double? Min = null,
    double? Max = null,
    double? Step = null
);

/// <summary>A single choice for a <c>select</c>/<c>multiselect</c> field: the wire <see cref="Value"/> and its display <see cref="Label"/>.</summary>
public sealed record WidgetSettingsFieldOption(string Value, string Label);
