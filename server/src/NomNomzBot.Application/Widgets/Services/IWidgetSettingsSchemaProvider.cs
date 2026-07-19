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

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The static catalogue of typed settings schemas — one per first-party widget type. It lets the dashboard render
/// a generic, schema-driven settings form for every first-party widget (no hand-editing of Vue source), and stays
/// correct per type without hand-maintained per-widget frontend code. Immutable reference data → registered as a
/// singleton.
/// </summary>
public interface IWidgetSettingsSchemaProvider
{
    /// <summary>Every first-party widget's settings schema, keyed order-stable by widget key.</summary>
    IReadOnlyList<WidgetSettingsSchema> GetAll();

    /// <summary>The schema for one first-party widget <paramref name="widgetKey"/> (e.g. <c>chat_box</c>), or null if none.</summary>
    WidgetSettingsSchema? GetByKey(string widgetKey);
}
