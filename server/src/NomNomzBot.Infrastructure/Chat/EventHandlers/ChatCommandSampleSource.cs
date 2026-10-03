// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
