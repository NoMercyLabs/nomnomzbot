// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Economy.Enums;

/// <summary>A channel's role in a shared savings jar (economy.md K.5).</summary>
public enum JarRole
{
    Owner,
    Partner,
    Viewer,
}

/// <summary>The lifecycle of a jar membership invite (economy.md K.5) — mutual-consent federation.</summary>
public enum JarMembershipStatus
{
    Pending,
    Accepted,
    Revoked,
}

/// <summary>The direction of a jar movement (economy.md K.6).</summary>
public enum JarMovementType
{
    Contribute,
    Withdraw,
}
