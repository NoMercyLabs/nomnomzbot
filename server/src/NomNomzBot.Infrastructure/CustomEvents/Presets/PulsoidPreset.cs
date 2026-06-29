// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.CustomEvents.Services;

namespace NomNomzBot.Infrastructure.CustomEvents.Presets;

/// <summary>Pulsoid heart-rate socket preset (custom-events.md D6).</summary>
internal sealed class PulsoidPreset : ICustomDataSourcePreset
{
    public string Key => "pulsoid";
    public string DisplayName => "Pulsoid (Heart Rate)";

    public CustomDataSourceTemplate Template { get; } =
        new(
            SourceKind: "socket",
            DefaultEndpointUrl: "wss://dev.pulsoid.net/api/v1/data/real_time?access_token=",
            AuthKindHint: "token_query_param",
            DefaultFieldMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bpm"] = "$.data.heart_rate",
                ["measured_at"] = "$.measured_at",
            }
        );
}
