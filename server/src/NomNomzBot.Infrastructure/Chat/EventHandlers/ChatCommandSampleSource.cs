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

using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// The chat command as a test-run trigger: a viewer's command message with no arguments, built by the same
/// <see cref="ChatMessageHandler.BuildInitialVariables"/> the live handler runs. The test run adds the request's
/// arguments as <c>args.N</c> and its role as <c>user.role</c>.
/// </summary>
public sealed class ChatCommandSampleSource : ITriggerSampleSource
{
    public TriggerSample Sample(DateTimeOffset now)
    {
        ChatMessageReceivedEvent chat = new()
        {
            BroadcasterId = Guid.Empty,
            MessageId = "sample-message",
            TwitchBroadcasterId = "100000001",
            UserId = "100000042",
            UserDisplayName = "SampleViewer",
            UserLogin = "sampleviewer",
            Message = "!command",
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
            OccurredAt = now,
        };

        return new(
            ChatCommandVariableKeys.Trigger,
            ChatCommandVariableKeys.Trigger,
            chat.UserId,
            chat.UserDisplayName,
            ChatMessageHandler.BuildInitialVariables(chat, string.Empty)
        );
    }
}
