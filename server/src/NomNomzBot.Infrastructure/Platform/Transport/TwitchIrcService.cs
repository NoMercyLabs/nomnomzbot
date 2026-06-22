// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Platform.Transport;

/// <summary>
/// Twitch IRC-over-WebSocket chat service.
/// Connects to wss://irc-ws.chat.twitch.tv, joins/parts channels, sends messages,
/// parses incoming IRC lines, and publishes domain events to IEventBus.
///
/// Rate limiting: 20 messages / 30 s (non-verified bot default).
/// Reconnects automatically with exponential back-off capped at 64 s.
/// </summary>
public sealed class TwitchIrcService : ITwitchChatService, IHostedService
{
    private const string IrcUrl = "wss://irc-ws.chat.twitch.tv:443";

    // Rate limit: 20 msgs / 30 s for non-verified bots — keep 2 in reserve
    private const int RateLimitBurst = 18;
    private const int RateLimitWindowMs = 30_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventBus _eventBus;
    private readonly TwitchOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TwitchIrcService> _logger;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    // Channels that should be joined; re-joined after reconnect
    private readonly ConcurrentDictionary<string, byte> _joinedChannels = new(
        StringComparer.OrdinalIgnoreCase
    );

    // Send-side rate limiting
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _tokenCount = RateLimitBurst;
    private DateTime _windowStart;

    public TwitchIrcService(
        IServiceScopeFactory scopeFactory,
        IEventBus eventBus,
        IOptions<TwitchOptions> options,
        TimeProvider timeProvider,
        ILogger<TwitchIrcService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _eventBus = eventBus;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _windowStart = _timeProvider.GetUtcNow().UtcDateTime;
    }

    // ─── IHostedService ───────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveLoop = Task.Run(() => RunWithReconnectAsync(_cts.Token), _cts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop;
            }
            catch (OperationCanceledException) { }
        }
    }

    // ─── ITwitchChatService ───────────────────────────────────────────────────────

    public async Task SendMessageAsync(
        string channelId,
        string message,
        CancellationToken ct = default
    )
    {
        await SendRawWithRateLimitAsync($"PRIVMSG #{channelId.ToLowerInvariant()} :{message}", ct);
    }

    public async Task SendReplyAsync(
        string channelId,
        string replyToMessageId,
        string message,
        CancellationToken ct = default
    )
    {
        await SendRawWithRateLimitAsync(
            $"@reply-parent-msg-id={replyToMessageId} PRIVMSG #{channelId.ToLowerInvariant()} :{message}",
            ct
        );
    }

    public async Task JoinChannelAsync(string channelName, CancellationToken ct = default)
    {
        string name = channelName.TrimStart('#').ToLowerInvariant();
        _joinedChannels.TryAdd(name, 0);
        await SendRawAsync($"JOIN #{name}", ct);
        _logger.LogInformation("IRC: Joined #{ChannelName}", name);
    }

    public async Task LeaveChannelAsync(string channelName, CancellationToken ct = default)
    {
        string name = channelName.TrimStart('#').ToLowerInvariant();
        _joinedChannels.TryRemove(name, out _);
        await SendRawAsync($"PART #{name}", ct);
        _logger.LogInformation("IRC: Left #{ChannelName}", name);
    }

    // ─── Connection loop ──────────────────────────────────────────────────────────

    private async Task RunWithReconnectAsync(CancellationToken ct)
    {
        TimeSpan delay = TimeSpan.FromSeconds(1);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                bool connected = await ConnectAndReceiveAsync(ct);
                if (!connected)
                {
                    // No bot token — poll every 60 s until one is available
                    await Task.Delay(TimeSpan.FromSeconds(60), ct);
                    continue;
                }

                // Reset backoff after a clean session
                delay = TimeSpan.FromSeconds(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IRC connection dropped, reconnecting in {Delay:g}", delay);
            }

            if (ct.IsCancellationRequested)
                break;

            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 64));
        }
    }

    /// <returns>false if no bot token is available (skip connection); true after session ends normally.</returns>
    private async Task<bool> ConnectAndReceiveAsync(CancellationToken ct)
    {
        string? token = await GetBotTokenAsync(ct);
        if (token is null)
            return false;

        _ws?.Dispose();
        _ws = new();

        _logger.LogInformation("IRC: Connecting to {Url}", IrcUrl);
        await _ws.ConnectAsync(new(IrcUrl), ct);
        _logger.LogInformation("IRC: Connected");

        await SendRawAsync("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership", ct);

        await SendRawAsync($"PASS oauth:{token}", ct);
        await SendRawAsync($"NICK {_options.BotUsername}", ct);

        // Re-join all tracked channels after reconnect
        foreach (string channel in _joinedChannels.Keys)
            await SendRawAsync($"JOIN #{channel}", ct);

        byte[] buffer = new byte[4096];
        StringBuilder sb = new();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            sb.Clear();
            WebSocketReceiveResult result;

            do
            {
                result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return true;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            foreach (
                string line in sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            )
                await HandleIrcLineAsync(line.TrimEnd('\r'), ct);
        }

        return true;
    }

    // ─── IRC dispatch ─────────────────────────────────────────────────────────────

    private async Task HandleIrcLineAsync(string line, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(line))
            return;

        if (line.StartsWith("PING"))
        {
            string pong = line.Length > 5 ? line[5..] : ":tmi.twitch.tv";
            await SendRawAsync($"PONG {pong}", ct);
            return;
        }

        (Dictionary<string, string> tags, _, string command, List<string> parameters) =
            ParseIrcLine(line);

        // IRC is for chat SEND only (self-host) + connection keepalive. Inbound chat/events (PRIVMSG,
        // USERNOTICE subs/gifts/raids/watch-streaks, CLEARCHAT/CLEARMSG) are NOT parsed here — they arrive via
        // EventSub (channel.chat.message / channel.subscribe / channel.chat.notification …). Parsing them from
        // IRC too would double-process every event. Only connection-control lines are handled.
        switch (command)
        {
            case "RECONNECT":
                _logger.LogWarning("IRC: Server requested RECONNECT");
                _ws?.Abort();
                break;

            case "001":
                _logger.LogInformation("IRC: Authenticated as {Username}", _options.BotUsername);
                break;
        }
    }

    // ─── Send helpers ─────────────────────────────────────────────────────────────

    private async Task SendRawWithRateLimitAsync(string line, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
            if ((now - _windowStart).TotalMilliseconds >= RateLimitWindowMs)
            {
                _tokenCount = RateLimitBurst;
                _windowStart = now;
            }

            if (_tokenCount <= 0)
            {
                int waitMs = (int)(RateLimitWindowMs - (now - _windowStart).TotalMilliseconds) + 50;
                if (waitMs > 0)
                {
                    _logger.LogDebug("IRC rate limit reached, pausing {WaitMs}ms", waitMs);
                    await Task.Delay(waitMs, ct);
                    _tokenCount = RateLimitBurst;
                    _windowStart = _timeProvider.GetUtcNow().UtcDateTime;
                }
            }

            _tokenCount--;
            await SendRawAsync(line, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendRawAsync(string line, CancellationToken ct)
    {
        if (_ws is not { State: WebSocketState.Open })
            return;

        byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    // ─── Token access ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the platform bot token (NomNomzBot) — stored with BroadcasterId=null.
    ///
    /// Per-channel white-label bots (BroadcasterId=channelId) require separate IRC connections
    /// and are not handled here. They use the Helix Chat API for message sending instead.
    /// </summary>
    private async Task<string?> GetBotTokenAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITokenProtector tokenProtector =
            scope.ServiceProvider.GetRequiredService<ITokenProtector>();

        // Platform bot only — BroadcasterId must be null
        Service? service = await db
            .Services.Where(s =>
                s.Name == "twitch_bot"
                && s.BroadcasterId == null
                && s.Enabled
                && s.AccessToken != null
            )
            .OrderByDescending(s => s.TokenExpiry)
            .FirstOrDefaultAsync(ct);

        if (service?.AccessToken is null)
        {
            _logger.LogWarning("IRC: No platform bot token found — will retry in 60 s");
            return null;
        }

        string? decrypted = await tokenProtector.TryUnprotectAsync(
            service.AccessToken,
            new TokenProtectionContext("_platform", "twitch_bot", "access"),
            ct
        );
        if (decrypted is null)
        {
            _logger.LogWarning(
                "IRC: Platform bot token could not be decrypted — will retry in 60 s"
            );
            return null;
        }

        return decrypted;
    }

    // ─── IRC parser ───────────────────────────────────────────────────────────────

    private static (
        Dictionary<string, string> Tags,
        string Prefix,
        string Command,
        List<string> Parameters
    ) ParseIrcLine(string line)
    {
        Dictionary<string, string> tags = new(StringComparer.Ordinal);
        string prefix = string.Empty;
        int pos = 0;

        // @tags
        if (line.Length > 0 && line[0] == '@')
        {
            int end = line.IndexOf(' ', 1);
            if (end < 0)
                return (tags, prefix, line, []);

            foreach (string tag in line[1..end].Split(';'))
            {
                int eq = tag.IndexOf('=');
                if (eq >= 0)
                    tags[tag[..eq]] = UnescapeTagValue(tag[(eq + 1)..]);
                else
                    tags[tag] = string.Empty;
            }

            pos = end + 1;
        }

        while (pos < line.Length && line[pos] == ' ')
            pos++;

        // :prefix
        if (pos < line.Length && line[pos] == ':')
        {
            int end = line.IndexOf(' ', pos + 1);
            prefix = end >= 0 ? line[(pos + 1)..end] : line[(pos + 1)..];
            pos = end >= 0 ? end + 1 : line.Length;
        }

        while (pos < line.Length && line[pos] == ' ')
            pos++;

        // command
        int cmdEnd = line.IndexOf(' ', pos);
        string command = cmdEnd >= 0 ? line[pos..cmdEnd] : line[pos..];
        pos = cmdEnd >= 0 ? cmdEnd + 1 : line.Length;

        // parameters
        List<string> parameters = new();
        while (pos < line.Length)
        {
            while (pos < line.Length && line[pos] == ' ')
                pos++;
            if (pos >= line.Length)
                break;

            if (line[pos] == ':')
            {
                parameters.Add(line[(pos + 1)..]);
                break;
            }

            int end = line.IndexOf(' ', pos);
            if (end < 0)
            {
                parameters.Add(line[pos..]);
                break;
            }
            parameters.Add(line[pos..end]);
            pos = end + 1;
        }

        return (tags, prefix, command, parameters);
    }

    private static IReadOnlyDictionary<string, string> ParseBadges(string? raw)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(raw))
            return result;

        foreach (string badge in raw.Split(','))
        {
            int slash = badge.IndexOf('/');
            if (slash >= 0)
                result[badge[..slash]] = badge[(slash + 1)..];
        }

        return result;
    }

    private static string UnescapeTagValue(string value) =>
        value
            .Replace("\\:", ";")
            .Replace("\\s", " ")
            .Replace("\\\\", "\\")
            .Replace("\\r", "\r")
            .Replace("\\n", "\n");
}
