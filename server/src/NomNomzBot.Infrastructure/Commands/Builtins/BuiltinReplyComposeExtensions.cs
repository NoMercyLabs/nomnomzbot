// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// One call per reply for a built-in that speaks through <see cref="IBuiltinResponseComposer"/>: it fills the
/// broadcaster and personality from the invocation context, so the built-in names only its reply group, slot,
/// neutral wording and variables.
/// </summary>
internal static class BuiltinReplyComposeExtensions
{
    public static Task<string> ComposeAsync(
        this IBuiltinResponseComposer composer,
        BuiltinCommandContext context,
        string builtinKey,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables = null,
        CancellationToken ct = default
    ) =>
        composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = builtinKey,
                Slot = slot,
                NeutralFallback = neutralFallback,
                Variables = variables,
            },
            ct
        );

    /// <summary>The "you may not use this command" line — the <c>system/permissiondenied</c> slot.</summary>
    public static Task<string> ComposePermissionDeniedAsync(
        this IBuiltinResponseComposer composer,
        BuiltinCommandContext context,
        CancellationToken ct = default
    ) =>
        composer.ComposeAsync(
            context,
            BuiltinResponseSlots.SystemReplies.Key,
            BuiltinResponseSlots.SystemReplies.PermissionDenied,
            "You don't have permission to use that command.",
            ct: ct
        );
}
