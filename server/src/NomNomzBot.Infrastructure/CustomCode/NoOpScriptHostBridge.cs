// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// A host bridge that resolves no capability (returns a null-yielding delegate). Used until the capability broker
/// lands — paired with an empty grant, so any side-effecting <c>bot.call</c> is denied at the executor's grant
/// check before the bridge is ever consulted.
/// </summary>
public sealed class NoOpScriptHostBridge : IScriptHostBridge
{
    public static NoOpScriptHostBridge Instance { get; } = new();

    private NoOpScriptHostBridge() { }

    public HostImportDelegate Resolve(string capabilityKey) => static (_, _, _) => null;
}
