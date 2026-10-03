// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Chat.ValueObjects;

/// <summary>One chat badge a user wears, such as moderator or subscriber.</summary>
/// <param name="SetId">The badge family, for example <c>moderator</c> or <c>subscriber</c>.</param>
/// <param name="Id">The version inside the family, for example the number of subscribed months.</param>
/// <param name="Info">Extra text for the badge, such as the exact subscribed months. Null when there is none.</param>
public sealed record ChatBadge(string SetId, string Id, string? Info = null);
