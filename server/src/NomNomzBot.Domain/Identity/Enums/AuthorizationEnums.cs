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

// The roles-permissions plane A/B enums (roles-permissions §1.1). All are [VC:enum] text-stored — the member
// name is the persisted token. Numeric ladder positions are mapped explicitly by AuthorizationLadder, never
// by the enum ordinal, so members may be reordered without shifting any stored level.

/// <summary>Plane B — channel-management ladder: <c>Moderator(10) &lt; LeadModerator(20) &lt; Editor(30) &lt; Broadcaster(40)</c>.</summary>
public enum ManagementRole
{
    Moderator,
    LeadModerator,
    Editor,
    Broadcaster,
}

/// <summary>Plane A — community-standing ladder: <c>Everyone(0) &lt; Subscriber(2) &lt; Vip(4) &lt; Artist(6) &lt; Moderator(10)</c>.</summary>
public enum CommunityStanding
{
    Everyone,
    Subscriber,
    Vip,
    Artist,
    Moderator,
}

/// <summary>Which plane an <c>ActionDefinition</c>'s floor sits in.</summary>
public enum AuthPlane
{
    Community,
    Management,
}

/// <summary>The danger tier of a capability — guards how low its floor may be set / whether it may be permit-granted.</summary>
public enum DangerTier
{
    Critical,
    Tos,
    Low,
}

/// <summary>How a <c>ChannelMembership</c> (Plane B role) was sourced.</summary>
public enum MembershipSource
{
    TwitchBadge,
    HelixEditors,
    BotGrant,
    Owner,
}

/// <summary>How a <c>ChannelCommunityStanding</c> (Plane A) was sourced.</summary>
public enum StandingSource
{
    ChatTags,
    EventSubBadge,
}

/// <summary>Whether a <c>PermitGrant</c> grants a whole role or a single capability.</summary>
public enum PermitGrantType
{
    Role,
    Capability,
}
