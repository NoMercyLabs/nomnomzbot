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
