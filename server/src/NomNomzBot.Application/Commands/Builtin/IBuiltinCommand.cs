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
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// A single code-defined built-in command. Implementations live in Infrastructure and are
/// registered in DI; the catalog is built from the resolved set at startup.
/// Built-in catalog membership is defined by code, never by DB seed rows.
/// </summary>
public interface IBuiltinCommand
{
    string BuiltinKey { get; }
    int DefaultCooldownSeconds { get; }
    int DefaultMinPermissionLevel { get; }

    /// <summary>
    /// A reserved built-in (the data-subject rights floor, gdpr-crypto.md §9) resolves BEFORE any
    /// authored channel command and cannot be shadowed, overridden, or disabled by a channel.
    /// Defaults to false — ordinary built-ins stay channel-toggleable.
    /// </summary>
    bool IsReserved => false;

    Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    );
}

/// <summary>Runtime invocation context for a built-in command execution.</summary>
public sealed class BuiltinCommandContext
{
    public required Guid BroadcasterId { get; init; }
    public required string TriggeringUserId { get; init; }
    public required string TriggeringUserDisplayName { get; init; }

    /// <summary>Twitch login (lowercase) of the caller — the get-or-create viewer-resolution key.</summary>
    public string TriggeringUserLogin { get; init; } = string.Empty;

    /// <summary>
    /// The caller's live badge level on the unified ladder (roles-permissions §0) — builtins that enforce
    /// a standing floor (e.g. a game's Permission) pass it through instead of re-resolving.
    /// </summary>
    public int RoleLevel { get; init; }

    /// <summary>Arguments after the command trigger (e.g. "!followage @someone" → "@someone").</summary>
    public string Args { get; init; } = string.Empty;

    /// <summary>
    /// When the triggering message is a reply, the parent message's plain text — lets a built-in capture the
    /// replied-to line directly (e.g. reply with <c>!quote add</c> to quote that message). Null for a
    /// non-reply message. Populated by the chat handler; a built-in only reads it.
    /// </summary>
    public string? ReplyParentMessageBody { get; init; }

    /// <summary>
    /// When the triggering message is a reply, the display name of the user being replied to — the natural
    /// attribution for a reply-capture built-in. Null for a non-reply message. Populated by the chat handler.
    /// </summary>
    public string? ReplyParentUserName { get; init; }

    /// <summary>
    /// The channel's explicit per-command response-template override, parsed from
    /// <c>ChannelBuiltinCommand.OverridesJson</c> by the chat handler. When set (non-blank) it WINS over the
    /// personality tone template; when null the built-in falls back to its tone template, then its neutral
    /// string. Populated by the handler — a built-in only reads it.
    /// </summary>
    public string? CustomResponseTemplate { get; init; }

    /// <summary>
    /// The channel's personality tone (<see cref="PersonalityTone"/>) — the voice the built-in phrases its
    /// response in. Defaults to <see cref="PersonalityTone.Informative"/>; set by the handler from the
    /// channel's resolved <c>Personality</c>.
    /// </summary>
    public string Personality { get; init; } = PersonalityTone.Informative;

    public CancellationToken CancellationToken { get; init; }
}
