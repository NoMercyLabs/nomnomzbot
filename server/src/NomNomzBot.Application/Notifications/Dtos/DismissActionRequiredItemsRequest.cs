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
