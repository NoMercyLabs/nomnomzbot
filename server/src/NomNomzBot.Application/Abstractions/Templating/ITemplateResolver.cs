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
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Simple synchronous resolve using only the provided variables (no async DB lookups).</summary>
    string Resolve(string template, IDictionary<string, string> variables);
}
