// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform.Exceptions;

namespace NomNomzBot.Domain.Commands.Exceptions;

public class CooldownActiveException : DomainException
{
    public TimeSpan RemainingCooldown { get; }

    public CooldownActiveException(string commandName, TimeSpan remaining)
        : base(
            $"Command '{commandName}' is on cooldown for {remaining.TotalSeconds:F0} more seconds."
        )
    {
        RemainingCooldown = remaining;
    }
}
