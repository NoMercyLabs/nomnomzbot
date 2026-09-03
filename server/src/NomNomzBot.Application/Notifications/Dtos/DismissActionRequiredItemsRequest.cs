// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Notifications.Dtos;

/// <summary>
/// Dismisses one or more action-required inbox items by their stable <see cref="ActionRequiredItemDto.Id"/>
/// keys (S-OWN22 T2). A grouped <c>held-user:{sourceUserId}</c> id is expanded server-side into one dismissal
/// per contained <c>held:{queueItemGuid}</c> key, so a NEW hold from that user surfaces again.
/// </summary>
public sealed record DismissActionRequiredItemsRequest
{
    public required List<string> Ids { get; init; }
}
