// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity.Enums;

/// <summary>
/// A chatter's role as seen in chat. <c>LeadModerator</c> is a Lead Moderator (Twitch's <c>lead_moderator</c> badge, which
/// replaces the regular moderator badge). Editor is not chat-visible, so it is absent here — see <c>ChatRole</c>.
/// </summary>
public enum UserRole
{
    Viewer,
    Subscriber,
    Vip,
    Moderator,
    LeadModerator,
    Broadcaster,
}
