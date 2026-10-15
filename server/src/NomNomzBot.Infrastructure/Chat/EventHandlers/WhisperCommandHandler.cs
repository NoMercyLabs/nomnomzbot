// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.Whispers;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Answers a viewer's whisper to the bot. Only a built-in flagged <see cref="IBuiltinCommand.WhisperSafe"/>
/// (read-only info for everyone) runs; its reply goes back as a whisper and never to chat. The channel is
/// the one named as the first argument (<c>!queue stoney_eagle</c>), else the one channel where the sender is
/// a chatter this session; with none or several the bot whispers back how to name one.
/// </summary>
public sealed class WhisperCommandHandler(
    IBuiltinCommandCatalog builtins,
    IChannelRegistry registry,
    ITwitchWhispersApi whispers,
    IChatProvider chat,
    IOutboundSanctionAccessor sanctions,
    WhisperRecipientCap cap,
    ILogger<WhisperCommandHandler> logger
) : IEventHandler<WhisperReceivedEvent>
{
    private const string Prefix = "!";

    public async Task HandleAsync(WhisperReceivedEvent @event, CancellationToken ct = default)
    {
        string text = @event.Text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal) || text.Length == Prefix.Length)
            return;

        string[] words = text[Prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IBuiltinCommand? builtin = builtins.Get(words[0].ToLowerInvariant());
        if (builtin is null)
            return;

        if (!builtin.WhisperSafe || builtin.DefaultMinPermissionLevel > 0)
        {
            await ReplyAsync(@event, null, $"{Prefix}{words[0]} works in chat only.", ct);
            return;
        }

        List<string> args = [.. words.Skip(1)];
        ChannelContext? channel = PickChannel(registry.GetAll(), args, @event.FromUserId);
        if (channel is null)
        {
            await ReplyAsync(
                @event,
                null,
                $"Which channel? Whisper me like {Prefix}{words[0]} <channel>.",
                ct
            );
            return;
        }

        BuiltinCommandContext context = new()
        {
            BroadcasterId = channel.BroadcasterId,
            TriggeringUserId = @event.FromUserId,
            TriggeringPlatform = "twitch",
            TriggeringUserDisplayName = @event.FromUserDisplayName,
            TriggeringUserLogin = @event.FromUserLogin,
            RoleLevel = 0,
            CommandPrefix = channel.CommandPrefix,
            Args = string.Join(' ', args),
            Personality = channel.Personality,
            CancellationToken = ct,
        };

        Result<string> result;
        using (
            sanctions.Begin(OutboundSanction.ChannelConfiguration($"whisper:{builtin.BuiltinKey}"))
        )
        {
            result = await builtin.ExecuteAsync(context, ct);
        }

        if (result.IsFailure || string.IsNullOrEmpty(result.Value))
        {
            logger.LogWarning(
                "Whispered {Builtin} from {User} gave no reply: {Error}",
                builtin.BuiltinKey,
                @event.FromUserLogin,
                result.ErrorMessage
            );
            return;
        }

        await ReplyAsync(@event, channel, result.Value, ct);
    }

    // The channel named as the first argument wins and is consumed; otherwise the sender's single channel.
    private static ChannelContext? PickChannel(
        IReadOnlyCollection<ChannelContext> channels,
        List<string> args,
        string fromUserId
    )
    {
        if (args.Count > 0)
        {
            string named = args[0].TrimStart('@', '#');
            ChannelContext? byName = channels.FirstOrDefault(c =>
                string.Equals(c.ChannelName, named, StringComparison.OrdinalIgnoreCase)
            );
            if (byName is not null)
            {
                args.RemoveAt(0);
                return byName;
            }
        }

        List<ChannelContext> shared = [.. ChannelsWithChatter(channels, fromUserId)];
        return shared.Count == 1 ? shared[0] : null;
    }

    private static IEnumerable<ChannelContext> ChannelsWithChatter(
        IReadOnlyCollection<ChannelContext> channels,
        string userId
    ) => channels.Where(c => c.SessionChatters.ContainsKey(userId));

    // Past Twitch's new-recipient cap the bot cannot whisper: it says so in the viewer's channel instead
    // (a short pointer, never the reply itself) so the viewer knows to ask in chat.
    private async Task ReplyAsync(
        WhisperReceivedEvent @event,
        ChannelContext? channel,
        string reply,
        CancellationToken ct
    )
    {
        if (!cap.TryAdmit(@event.FromUserId))
        {
            ChannelContext? where =
                channel
                ?? ChannelsWithChatter(registry.GetAll(), @event.FromUserId).FirstOrDefault();
            if (where is not null)
                await chat.SendMessageAsync(
                    where.BroadcasterId,
                    $"@{@event.FromUserLogin} I hit Twitch's daily whisper limit for new people. Ask in chat instead.",
                    ct
                );
            return;
        }

        using IDisposable sanction = sanctions.Begin(
            OutboundSanction.ChannelConfiguration("whisper-reply")
        );
        Result sent = await whispers.SendAsBotAsync(
            channel?.BroadcasterId ?? Guid.Empty,
            @event.ToUserId,
            @event.FromUserId,
            reply,
            ct
        );
        if (sent.IsFailure)
        {
            logger.LogWarning(
                "Whisper reply to {User} failed: {Error}",
                @event.FromUserLogin,
                sent.ErrorMessage
            );
        }
    }
}
