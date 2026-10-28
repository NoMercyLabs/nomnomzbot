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

using NomNomzBot.Application.CustomEvents.Services;

namespace NomNomzBot.Infrastructure.CustomEvents.Presets;

/// <summary>HypeRate heart-rate socket preset (custom-events.md D6).</summary>
internal sealed class HypeRatePreset : ICustomDataSourcePreset
{
    public string Key => "hyperate";
    public string DisplayName => "HypeRate (Heart Rate)";

    public CustomDataSourceTemplate Template { get; } =
        new(
            SourceKind: "socket",
            DefaultEndpointUrl: "wss://app.hyperate.io/socket/websocket?token=",
            AuthKindHint: "token_query_param",
            DefaultFieldMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bpm"] = "$.hr",
            }
        );
}
