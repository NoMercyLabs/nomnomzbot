// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// Finds the triggers that really run a script, so the editor can type <c>bot.getVar</c> for them. A script runs
/// through <c>run_code</c> steps; each step sits in a pipeline; a pipeline is started by a chat command, an event
/// response or a pipeline trigger row. Tenant from <c>ICurrentTenantService</c>.
/// </summary>
public interface ICodeScriptTriggerResolver
{
    /// <summary>
    /// The distinct, sorted SDK trigger keys of every pipeline that runs <paramref name="codeScriptId"/>: <c>command</c>
    /// for a chat command, the event type for an event trigger. Timer, manual and webhook triggers set no keys and add
    /// none. An empty list means no known trigger. <c>NOT_FOUND</c> when the script is not in this channel.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetTriggerKeysAsync(
        Guid codeScriptId,
        CancellationToken cancellationToken = default
    );
}
