// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
