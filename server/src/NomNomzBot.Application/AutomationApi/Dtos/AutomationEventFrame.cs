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

using System.Text.Json.Serialization;

namespace NomNomzBot.Application.AutomationApi.Dtos;

public sealed record AutomationEventFrame(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("broadcasterId")] string BroadcasterId,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("data")] object Data
);

public sealed record AutomationHistoryPage(
    IReadOnlyList<AutomationEventFrame> Items,
    string? NextCursor
);
