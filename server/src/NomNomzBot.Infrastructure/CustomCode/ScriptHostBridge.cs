// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Contracts.CustomCode;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The per-execution host-dispatch bridge (custom-code.md §3.1/§6.2) — the only path from a granted <c>bot.*</c>
/// import to host code. Bound to exactly one <c>BroadcasterId</c> (host-side; never readable by the guest); each
/// resolved delegate is primitive-in / primitive-out and tenant-scoped to that channel. <c>chat.send</c>/
/// <c>chat.reply</c> are wired to the channel's chat transport (the bot token lives host-side, never in the guest).
/// (Deferred — documented: music.queue / http.fetch / economy.read dispatch; those granted caps currently no-op
/// until wired — the grant still gates whether the script may call them at all.)
/// </summary>
public sealed class ScriptHostBridge(
    Guid broadcasterId,
    ITwitchChatService chatService,
    ITwitchIdentityResolver identityResolver
) : IScriptHostBridge
{
    public HostImportDelegate Resolve(string capabilityKey) =>
        capabilityKey switch
        {
            "chat.send" or "chat.reply" => SendChat,
            _ => static (_, _, _) => null, // granted-but-unwired caps no-op; the grant already gated access
        };

    private string? SendChat(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return null;

        // The guest holds only the Guid; the Twitch channel id + bot token are resolved host-side.
        string? channelId = identityResolver
            .GetTwitchChannelIdAsync(broadcasterId, ct)
            .GetAwaiter()
            .GetResult();
        if (channelId is null)
            return null;

        chatService.SendMessageAsync(channelId, args[0], ct).GetAwaiter().GetResult();
        return null;
    }
}
