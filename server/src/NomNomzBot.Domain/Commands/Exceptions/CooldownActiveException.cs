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
