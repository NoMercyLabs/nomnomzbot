// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using NomNomzBot.Application.Economy.Services;

namespace NomNomzBot.Infrastructure.Economy;

/// <summary>
/// The production <see cref="IGameRandomizer"/> — a cryptographically-secure source (economy.md §3.5). Draws 53
/// random bits and scales them into <c>[0, 1)</c> (the IEEE-754 double mantissa), so the distribution is exact.
/// </summary>
public sealed class CsprngGameRandomizer : IGameRandomizer
{
    public double NextUnitInterval()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        ulong value = BitConverter.ToUInt64(bytes);
        return (value >> 11) * (1.0 / (1UL << 53));
    }
}
