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
    HelixSeed,

    /// <summary>Set by the broadcaster (<c>!whitelist</c>); no sync path ever overwrites, lowers or deletes it.</summary>
    Manual,
}

/// <summary>Whether a <c>PermitGrant</c> grants a whole role or a single capability.</summary>
public enum PermitGrantType
{
    Role,
    Capability,
}
