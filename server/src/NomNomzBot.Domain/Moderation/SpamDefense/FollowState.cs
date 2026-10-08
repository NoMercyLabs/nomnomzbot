// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>
/// Whether a viewer follows the channel, as far as we could find out. Three values on purpose: a failed
/// lookup, a missing <c>moderator:read:followers</c> scope, or a platform with no follow-date API
/// (Kick, YouTube) is <see cref="Unknown"/> — never <see cref="NotFollowing"/>.
/// </summary>
public enum FollowState
{
    /// <summary>We could not find out. Earns no follow-based tier and adds no risk mark.</summary>
    Unknown = 0,

    /// <summary>The platform confirmed the viewer does not follow.</summary>
    NotFollowing = 1,

    /// <summary>The platform confirmed the viewer follows, and when.</summary>
    Following = 2,
}
