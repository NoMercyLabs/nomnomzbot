// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.CustomEvents.Services;

/// <summary>
/// Auto-discovered preset descriptor (custom-events.md D6). Each preset pre-fills a <c>CustomDataSource</c>
/// form with provider-specific defaults. Adding a preset = drop a descriptor — no engine edit required.
/// </summary>
public interface ICustomDataSourcePreset
{
    /// <summary>Stable lowercase identifier (e.g. <c>pulsoid</c>, <c>hyperate</c>).</summary>
    string Key { get; }

    string DisplayName { get; }

    CustomDataSourceTemplate Template { get; }
}

/// <summary>
/// The editable defaults a preset contributes — the streamer can override any field after choosing the preset.
/// </summary>
public sealed record CustomDataSourceTemplate(
    string SourceKind,
    string? DefaultEndpointUrl,
    string? AuthKindHint,
    IReadOnlyDictionary<string, string> DefaultFieldMap
);
