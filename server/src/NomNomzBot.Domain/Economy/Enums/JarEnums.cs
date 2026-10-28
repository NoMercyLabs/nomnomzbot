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
