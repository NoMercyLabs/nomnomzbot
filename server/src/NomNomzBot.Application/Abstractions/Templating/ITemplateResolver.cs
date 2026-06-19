// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Resolves template strings by replacing placeholders with context values.
/// Example: "Thanks for following, {user}!" -> "Thanks for following, Stoney_Eagle!"
/// </summary>
public interface ITemplateResolver
{
    /// <summary>
    /// Resolves a template string using the provided context dictionary and optional channel context.
    /// Pre-seeded variables in <paramref name="seedVariables"/> take precedence over auto-resolved values.
    /// </summary>
    Task<string> ResolveAsync(
        string template,
        IDictionary<string, string> seedVariables,
        string? broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Simple synchronous resolve using only the provided variables (no async DB lookups).</summary>
    string Resolve(string template, IDictionary<string, string> variables);
}
