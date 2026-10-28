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

using System.Text.Json.Nodes;

namespace NomNomzBot.Application.DevPlatform.Dtos;

/// <summary>
/// One row of the developer-facing event catalog (dev-platform.md §8, <c>GET /api/v1/sdk/event-catalog</c>):
/// a visible event's stable wire name, its visibility tier, and the reflected JSON Schema of its payload.
/// </summary>
/// <param name="WireName">The stable dotted wire name, e.g. <c>chat.message</c>.</param>
/// <param name="Tier">The visibility tier name (<c>Public</c> / <c>Moderator</c> / <c>Broadcaster</c>).</param>
/// <param name="PayloadSchema">The JSON Schema describing the event payload shape.</param>
/// <param name="SamplePayloadJson">
/// A real, verified example wire payload for this event (copied verbatim from the corresponding translator's own
/// behaviour test fixture), or <c>null</c> when no verified real fixture exists yet for this event — never a
/// fabricated approximation.
/// </param>
public sealed record EventCatalogItemDto(
    string WireName,
    string Tier,
    JsonNode PayloadSchema,
    string? SamplePayloadJson = null
);
