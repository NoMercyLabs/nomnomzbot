// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// Resolves the winning response template (override → personality tone → neutral fallback) and renders it
/// through <see cref="ITemplateResolver"/> so every built-in response speaks in the channel's voice with the
/// full template-variable set available. The single home of the precedence rule.
/// </summary>
public sealed class BuiltinResponseComposer : IBuiltinResponseComposer
{
    private readonly ITemplateResolver _templates;

    public BuiltinResponseComposer(ITemplateResolver templates)
    {
        _templates = templates;
    }

    public async Task<string> ComposeAsync(
        BuiltinResponseRequest request,
        CancellationToken cancellationToken = default
    )
    {
        // Precedence: explicit per-command override, then a tone variation, then the neutral fallback.
        string template =
            request.OverrideTemplate is { Length: > 0 } over && !string.IsNullOrWhiteSpace(over)
                ? over
                : ToneTemplateCatalog.Pick(request.Personality, request.BuiltinKey, request.Slot)
                    ?? request.NeutralFallback;

        if (string.IsNullOrEmpty(template))
            return string.Empty;

        // ITemplateResolver takes a mutable IDictionary; copy the caller's read-only bag into one. The
        // resolver treats these as precedence seeds (never mutates them), and copies again internally.
        Dictionary<string, string> variables = request.Variables is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(request.Variables, StringComparer.OrdinalIgnoreCase);

        return await _templates.ResolveAsync(
            template,
            variables,
            request.BroadcasterId,
            cancellationToken
        );
    }
}
