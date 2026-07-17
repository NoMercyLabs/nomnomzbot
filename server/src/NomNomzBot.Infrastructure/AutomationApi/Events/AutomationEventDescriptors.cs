// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Domain.Supporters.Events;

namespace NomNomzBot.Infrastructure.AutomationApi.Events;

// The seed set of public automation events (automation-api.md D6). Each projection is the PII-safe
// wire shape — anonymous objects serialized camelCase; internal row ids, raw payloads, and anything
// an integrator has no business seeing stay out. Adding an event to the public surface = add a
// descriptor here (or beside its module); the DI scan picks it up.

/// <summary>Chat message (EventSub channel.chat.message) → <c>Twitch.ChatMessage</c>.</summary>
public sealed class ChatMessageEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "Twitch.ChatMessage";
    public string Description => "A chat message was received in the channel.";
    public Type DomainEventType => typeof(ChatMessageReceivedEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        ChatMessageReceivedEvent e = (ChatMessageReceivedEvent)domainEvent;
        return new
        {
            messageId = e.MessageId,
            userLogin = e.UserLogin,
            userDisplayName = e.UserDisplayName,
            message = e.Message,
            isSubscriber = e.IsSubscriber,
            isVip = e.IsVip,
            isModerator = e.IsModerator,
            isBroadcaster = e.IsBroadcaster,
            bits = e.Bits,
        };
    }
}

/// <summary>Stream went live (EventSub stream.online) → <c>Stream.Online</c>.</summary>
public sealed class StreamOnlineEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "Stream.Online";
    public string Description => "The channel's stream went live.";
    public Type DomainEventType => typeof(ChannelOnlineEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        ChannelOnlineEvent e = (ChannelOnlineEvent)domainEvent;
        return new
        {
            broadcasterDisplayName = e.BroadcasterDisplayName,
            title = e.StreamTitle,
            game = e.GameName,
            startedAt = e.StartedAt,
        };
    }
}

/// <summary>Stream ended (EventSub stream.offline) → <c>Stream.Offline</c>.</summary>
public sealed class StreamOfflineEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "Stream.Offline";
    public string Description => "The channel's stream ended.";
    public Type DomainEventType => typeof(ChannelOfflineEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        ChannelOfflineEvent e = (ChannelOfflineEvent)domainEvent;
        return new
        {
            broadcasterDisplayName = e.BroadcasterDisplayName,
            streamDurationSeconds = (long)e.StreamDuration.TotalSeconds,
        };
    }
}

/// <summary>Incoming raid (EventSub channel.raid) → <c>Twitch.RaidReceived</c>.</summary>
public sealed class RaidReceivedEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "Twitch.RaidReceived";
    public string Description => "Another channel raided this channel.";
    public Type DomainEventType => typeof(RaidReceivedEvent);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        RaidReceivedEvent e = (RaidReceivedEvent)domainEvent;
        return new { fromDisplayName = e.FromDisplayName, viewerCount = e.ViewerCount };
    }
}

/// <summary>Normalized monetization event (supporter-events.md) → <c>Supporter.Received</c>.</summary>
public sealed class SupporterReceivedEventDescriptor : IAutomationEventDescriptor
{
    public string PublicName => "Supporter.Received";
    public string Description =>
        "A supporter event (tip / membership / merch / charity) was received.";
    public Type DomainEventType => typeof(SupporterEventReceived);

    public object ProjectPayload(DomainEventBase domainEvent)
    {
        SupporterEventReceived e = (SupporterEventReceived)domainEvent;
        // PII-safe: display fields only — the internal row id, supporter user id, and source key stay internal.
        return new
        {
            kind = e.Kind,
            supporterDisplayName = e.SupporterDisplayName,
            amountMinor = e.AmountMinor,
            currency = e.Currency,
            tier = e.Tier,
            quantity = e.Quantity,
            message = e.MessageText,
            isRecurring = e.IsRecurring,
        };
    }
}
