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

using NomNomzBot.Application.Chat.Services;
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

    /// <summary>Seconds one shared (global) cooldown holds the whole channel. 0 = no global cooldown.</summary>
    int DefaultCooldownSeconds { get; }

    /// <summary>Seconds each chatter is held on their own clock. 0 (default) = no per-user cooldown.</summary>
    int DefaultUserCooldownSeconds => 0;
    int DefaultMinPermissionLevel { get; }

    /// <summary>
    /// A reserved built-in (the data-subject rights floor, gdpr-crypto.md §9) resolves BEFORE any
    /// authored channel command and cannot be shadowed, overridden, or disabled by a channel.
    /// Defaults to false — ordinary built-ins stay channel-toggleable.
    /// </summary>
    bool IsReserved => false;

    /// <summary>
    /// A system command that is always on: no channel can turn it off and no channel command can shadow it,
    /// but unlike a reserved one its cooldown, floor and replies stay the channel's. Every reserved built-in is
    /// always on.
    /// </summary>
    bool IsAlwaysOn => IsReserved;

    /// <summary>
    /// Whether this caller may use the command at all, for builtins that refuse inside
    /// <see cref="ExecuteAsync"/> on a grant rather than the level floor. The <c>!commands</c> / <c>!help</c>
    /// listing asks this so it shows only what the caller can really run. Defaults to true.
    /// </summary>
    Task<bool> IsAvailableToAsync(BuiltinCommandContext context, CancellationToken ct = default) =>
        Task.FromResult(true);

    /// <summary>
    /// True when the built-in only reads information any viewer may see, so it may also run from a whisper
    /// (its reply goes back as a whisper, never to chat). Defaults to false: a command stays chat-only until
    /// it is explicitly marked.
    /// </summary>
    bool WhisperSafe => false;

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

    /// <summary>The provider key (<c>AuthEnums.Platform</c>) <see cref="TriggeringUserId"/> belongs to; <c>null</c> when the id is internal or the invoker has no platform (the dashboard, a test).</summary>
    public string? TriggeringPlatform { get; init; }
    public required string TriggeringUserDisplayName { get; init; }

    /// <summary>Twitch login (lowercase) of the caller — the get-or-create viewer-resolution key.</summary>
    public string TriggeringUserLogin { get; init; } = string.Empty;

    /// <summary>How chat text names the caller: the login when the display name is not the login apart from case, so Twitch links the mention.</summary>
    public string TriggeringUserChatName =>
        ChatMentionName.For(TriggeringUserDisplayName, TriggeringUserLogin);

    /// <summary>
    /// The id of the chat message that triggered this command, so a builtin can enrich THAT line in the
    /// overlay after doing its work — a song request replacing "!sr some song" with the resolved track's
    /// card. Empty when the builtin was invoked from somewhere other than a chat message (the dashboard,
    /// a test), in which case there is no line to enrich and enrichment is skipped.
    /// </summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>
    /// The caller's EFFECTIVE level on the unified ladder (roles-permissions §0): MAX(live badge, bot-granted
    /// role, active permit) — the same level the command gate used. Builtins that enforce a standing floor
    /// (e.g. a game's Permission) pass it through instead of re-resolving.
    /// </summary>
    public int RoleLevel { get; init; }

    /// <summary>The channel's command prefix (<c>Channel.CommandPrefix</c>), so a listing shows triggers as typed.</summary>
    public string CommandPrefix { get; init; } = "!";

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

    /// <summary>When the triggering message is a reply, the id of the message being replied to. Null for a non-reply message.</summary>
    public string? ReplyParentMessageId { get; init; }

    /// <summary>When the triggering message is a reply, the login of the user being replied to. Null for a non-reply message.</summary>
    public string? ReplyParentUserLogin { get; init; }

    /// <summary>
    /// When the triggering message is a reply, the display name of the user who started the thread. A reply to
    /// the bot's answer has the bot as its parent and the viewer the bot answered as its thread starter.
    /// </summary>
    public string? ReplyThreadUserName { get; init; }

    /// <summary>
    /// The channel's personality tone (<see cref="PersonalityTone"/>) — the voice the built-in phrases its
    /// response in. Defaults to <see cref="PersonalityTone.Informative"/>; set by the handler from the
    /// channel's resolved <c>Personality</c>.
    /// </summary>
    public string Personality { get; init; } = PersonalityTone.Informative;

    /// <summary>
    /// The channel's per-command "speak with TTS" override, parsed from
    /// <c>ChannelBuiltinCommand.OverridesJson</c> by the chat handler (default off). A built-in that supports
    /// TTS (e.g. <c>!quote</c>, S-OBS-12) reads this to decide whether to also queue its reply through
    /// <c>ITtsDispatchService</c> — never claim it "spoke" when this is false, and never let a TTS failure
    /// (disabled channel, no overlay/voice) affect the chat reply. Populated by the handler — a built-in only
    /// reads it.
    /// </summary>
    public bool SpeakWithTts { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
