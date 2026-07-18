// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
public sealed record EventCatalogItemDto(string WireName, string Tier, JsonNode PayloadSchema);
