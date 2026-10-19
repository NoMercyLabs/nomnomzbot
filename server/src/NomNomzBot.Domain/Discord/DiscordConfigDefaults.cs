// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Discord;

/// <summary>
/// The one place each Discord setting's default is written. The entity initializers, the EF column
/// defaults, the create requests and the config services all read these values. A channel with no
/// Discord rule resolves to an empty list: no rule means no Discord post, by design.
/// </summary>
public static class DiscordConfigDefaults
{
    /// <summary>A new notification rule starts enabled.</summary>
    public const bool NotificationEnabled = true;

    /// <summary>A new live-role rule starts enabled.</summary>
    public const bool LiveRoleEnabled = true;

    /// <summary>The current shape version of the stored embed JSON. Only a breaking reshape raises it.</summary>
    public const int EmbedConfigVersion = 1;
}
