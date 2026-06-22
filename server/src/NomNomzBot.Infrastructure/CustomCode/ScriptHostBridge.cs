// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http;
using System.Text;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// The per-execution host-dispatch bridge (custom-code.md §3.1/§6.2) — the only path from a granted <c>bot.*</c>
/// import to host code. Bound to exactly one <c>BroadcasterId</c> (host-side; never readable by the guest); each
/// resolved delegate is primitive-in / primitive-out and tenant-scoped to that channel. <c>chat.send</c>/
/// <c>chat.reply</c> dispatch to the channel's chat transport (bot token host-side, never in the guest);
/// <c>economy.read</c> reads this channel's ledger; <c>music.queue</c> enqueues a request; <c>http.fetch</c> does a
/// capped GET through the SSRF-hardened egress client. Every dispatch fails closed (returns a safe primitive).
/// </summary>
public sealed class ScriptHostBridge(
    Guid broadcasterId,
    string triggeringUserId,
    ITwitchChatService chatService,
    ITwitchIdentityResolver identityResolver,
    ICurrencyAccountService currencyService,
    IMusicService musicService,
    IHttpClientFactory httpClientFactory
) : IScriptHostBridge
{
    private const int MaxResponseBytes = 256 * 1024;

    public HostImportDelegate Resolve(string capabilityKey) =>
        capabilityKey switch
        {
            "chat.send" or "chat.reply" => SendChat,
            "economy.read" => ReadBalance,
            "music.queue" => QueueMusic,
            "http.fetch" => Fetch,
            _ => static (_, _, _) => null, // granted-but-unwired caps no-op; the grant already gated access
        };

    private string? Fetch(string capabilityKey, IReadOnlyList<string> args, CancellationToken ct)
    {
        if (
            args.Count == 0
            || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
        )
            return null;

        try
        {
            // The egress client resolves-then-pins + blocks non-public IPs + is https-only (SSRF-hardened);
            // bounded by the script's cancellation budget. Read is capped so a huge body can't flood the guest.
            HttpClient client = httpClientFactory.CreateClient(EgressHttpClient.Name);
            using HttpResponseMessage response = client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
                .GetAwaiter()
                .GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            using System.IO.Stream body = response.Content.ReadAsStream(ct);
            byte[] buffer = new byte[MaxResponseBytes];
            int total = 0;
            int read;
            while (
                total < MaxResponseBytes
                && (read = body.Read(buffer, total, MaxResponseBytes - total)) > 0
            )
                total += read;
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch
        {
            return null; // blocked egress / timeout / transport fault — fail closed
        }
    }

    private string? QueueMusic(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        if (args.Count == 0 || string.IsNullOrWhiteSpace(args[0]))
            return "false";

        // The music service takes the raw query (title/artist/link) and resolves + enqueues it host-side,
        // attributing the request to the trigger user; the bot's provider token never reaches the guest.
        bool queued = musicService
            .AddToQueueAsync(broadcasterId.ToString(), args[0], triggeringUserId, ct)
            .GetAwaiter()
            .GetResult();
        return queued ? "true" : "false";
    }

    private string? ReadBalance(
        string capabilityKey,
        IReadOnlyList<string> args,
        CancellationToken ct
    )
    {
        // The optional userId arg names a viewer of THIS channel; default to the trigger user (host-validated).
        string subject =
            args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : triggeringUserId;
        if (!Guid.TryParse(subject, out Guid viewerUserId))
            return "0";

        Result<long> balance = currencyService
            .GetBalanceAsync(broadcasterId, viewerUserId, ct)
            .GetAwaiter()
            .GetResult();
        return balance.IsSuccess ? balance.Value.ToString() : "0";
    }

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
