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

namespace NomNomzBot.Domain.Moderation.Entities;

/// <summary>
/// The one place each shared-ban default is written. A channel with no saved <see cref="SharedBanSettings"/>
/// row resolves to these values; the entity and the service fallbacks all read them. Both switches are
/// opt-in: a channel neither accepts nor shares bans until its owner turns them on.
/// </summary>
public static class SharedBanDefaults
{
    public const bool AcceptSharedChatBans = false;

    public const bool ShareOutgoingBans = false;
}
