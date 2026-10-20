// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The journal stores each event under the bot's own name for it (FollowEvent). These are the kinds a streamer
// looks for; each has a plain phrase in the page words (events.<name>) and is a choice of the kind filter.
export const EVENT_KINDS = [
  'FollowEvent',
  'NewSubscriptionEvent',
  'ResubscriptionEvent',
  'GiftSubscriptionEvent',
  'SubscriptionEndedEvent',
  'CheerEvent',
  'BitsUsedEvent',
  'RaidEvent',
  'OutgoingRaidEvent',
  'ChatMessageReceivedEvent',
  'ChatMessageDeletedEvent',
  'ChatClearedEvent',
  'FirstTimeChatterDetectedEvent',
  'ReturningChatterDetectedEvent',
  'CommandExecutedEvent',
  'RewardRedeemedEvent',
  'UserBannedEvent',
  'UserUnbannedEvent',
  'UserTimedOutEvent',
  'WarningSentEvent',
  'ModerationActionTakenEvent',
  'ModeratorAddedEvent',
  'ModeratorRemovedEvent',
  'VipAddedEvent',
  'VipRemovedEvent',
  'ChannelOnlineEvent',
  'ChannelOfflineEvent',
  'ChannelUpdatedEvent',
  'PollBeganEvent',
  'PollEndedEvent',
  'PredictionBeganEvent',
  'PredictionEndedEvent',
  'HypeTrainBeganEvent',
  'HypeTrainEndedEvent',
  'ShoutoutSentEvent',
  'ShoutoutReceivedEvent',
  'SongRequestedEvent',
  'SongSkippedEvent',
  'QuoteAddedEvent',
  'AdBreakBeganEvent',
  'CharityDonationEvent',
  'GiveawayDrawnEvent',
  'WatchStreakMilestoneEvent',
] as const;

export type EventKind = (typeof EVENT_KINDS)[number];

// Where an event came from, as the journal stores it. Each has a plain name in the page words (sources.<name>).
export const EVENT_SOURCES = [
  'eventsub',
  'irc',
  'domain',
  'webhook',
  'federation',
  'import',
] as const;

export type EventSource = (typeof EVENT_SOURCES)[number];

// The bot's own bookkeeping: a person did not do these, so the list hides them unless asked.
const HOUSEKEEPING_KINDS = [
  'EventSubNotificationJournaledEvent',
  'LedgerEntryRecordedEvent',
  'CurrencyCreditedEvent',
  'CurrencyDebitedEvent',
  'CurrencyEarnedEvent',
  'AdScheduleUpdatedEvent',
  'IntegrationTokenRefreshedEvent',
  'CommunityStandingChangedEvent',
  'TtsUtteranceQueuedEvent',
  'TtsUtteranceReviewedEvent',
] as const;

// A dotted name is Twitch's raw notice as it arrived; the bot's own reading of it is the event a person looks for.
export function isHousekeeping(type: string): boolean {
  return type.includes('.') || HOUSEKEEPING_KINDS.some((kind) => kind === type);
}

export function isEventKind(type: string): type is EventKind {
  return EVENT_KINDS.some((kind) => kind === type);
}

export function isEventSource(source: string): source is EventSource {
  return EVENT_SOURCES.some((known) => known === source);
}

// Short names a person knows by their letters, written as letters wherever they stand in an event name.
const LETTER_WORDS = ['obs', 'tts', 'vip'] as const;

// A kind with no phrase reads as its own name in words: ObsStreamingStateChangedEvent -> "OBS streaming state changed",
// channel.chat.message -> "Channel chat message".
export function spokenEventName(type: string): string {
  const words = type
    .replace(/Event$/, '')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[._]+/g, ' ')
    .toLowerCase()
    .split(' ')
    .map((word) =>
      LETTER_WORDS.some((letters) => letters === word)
        ? word.toUpperCase()
        : word)
    .join(' ');

  return words.charAt(0).toUpperCase() + words.slice(1);
}
