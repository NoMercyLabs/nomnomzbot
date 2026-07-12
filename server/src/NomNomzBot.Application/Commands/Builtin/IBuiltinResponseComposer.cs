// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// Renders a built-in command's response by resolving the ONE template that wins the precedence ladder and
/// filling it through the template resolver. The single place personality precedence lives, so every
/// response built-in phrases itself identically:
///
/// <list type="number">
///   <item>the channel's explicit per-command override (<c>ChannelBuiltinCommand.OverridesJson</c>), if set;</item>
///   <item>a random variation from the channel's personality tone for <c>(tone, builtinKey, slot)</c>;</item>
///   <item>the built-in's own neutral fallback string.</item>
/// </list>
/// </summary>
public interface IBuiltinResponseComposer
{
    /// <summary>Resolves the winning template for the request and renders it with the supplied variables.</summary>
    Task<string> ComposeAsync(
        BuiltinResponseRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Inputs for one built-in response render — see <see cref="IBuiltinResponseComposer"/> for precedence.</summary>
public sealed record BuiltinResponseRequest
{
    /// <summary>Tenant whose registry/DB the template resolver reads variables from.</summary>
    public required Guid BroadcasterId { get; init; }

    /// <summary>The channel's personality tone token (<c>PersonalityTone.*</c>).</summary>
    public required string Personality { get; init; }

    /// <summary>Catalog key for the built-in (e.g. "uptime", "song").</summary>
    public required string BuiltinKey { get; init; }

    /// <summary>The response case within the built-in (e.g. "live"/"offline") — the tone catalog slot.</summary>
    public required string Slot { get; init; }

    /// <summary>
    /// The channel's explicit per-command override template for this response, or null. Wins over the tone
    /// template when non-blank (populated by the handler from <c>ChannelBuiltinCommand.OverridesJson</c>).
    /// </summary>
    public string? OverrideTemplate { get; init; }

    /// <summary>Neutral fallback used when neither an override nor a tone template exists — never null.</summary>
    public required string NeutralFallback { get; init; }

    /// <summary>Built-in-computed variables (real values) seeded into the render; take precedence over auto-resolved.</summary>
    public IReadOnlyDictionary<string, string>? Variables { get; init; }
}
