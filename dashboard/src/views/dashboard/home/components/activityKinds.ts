// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue'

import type { ActivityEvent } from '@/api/home'

import BirthdayCake from '~icons/nnz/birthday-cake'
import ChatBubble from '~icons/nnz/chat-bubble'
import Diamond from '~icons/nnz/diamond'
import Fire from '~icons/nnz/fire'
import GiftCard from '~icons/nnz/gift-card'
import Hammer from '~icons/nnz/hammer'
import HeartBroken from '~icons/nnz/heart-broken'
import Megaphone from '~icons/nnz/megaphone'
import Moderator from '~icons/nnz/moderator'
import PaperPlaneSend from '~icons/nnz/paper-plane-send'
import PresentationChart from '~icons/nnz/presentation-chart'
import Rocket from '~icons/nnz/rocket'
import Star from '~icons/nnz/star'
import SwordsCrossing from '~icons/nnz/swords-crossing'
import Ticket from '~icons/nnz/ticket'
import Timer from '~icons/nnz/timer'
import Trophy from '~icons/nnz/trophy'
import UserAdd from '~icons/nnz/user-add'
import UserRemove from '~icons/nnz/user-remove'
import Wave from '~icons/nnz/wave'

import { parseActivityData, pickCount, pickText, tierOf, type ActivityData } from './activityData'

// Text for the panel to translate: an i18n key with optional params.
export type ActivityRowText = { key: string; params?: Record<string, string | number> }
export type ActivityRowView = {
  icon: Component
  title: ActivityRowText | null
  detail: ActivityRowText
}

type Describe = (data: ActivityData) => { title?: ActivityRowText; detail: ActivityRowText } | null
type ActivityKind = { icon: Component; label: string; describe?: Describe }

const TYPE: string = 'dashboard.home.activity.type.'
const DETAIL: string = 'dashboard.home.activity.detail.'
const detail = (name: string, params?: Record<string, string | number>): ActivityRowText => ({
  key: DETAIL + name,
  params,
})

function subscribed(data: ActivityData): ReturnType<Describe> {
  const tier = tierOf(data.tier)

  if (tier === null)
    return null

  const months = pickCount(data, 'cumulativeMonths', 'months')

  if (months !== null && months > 1)
    return { detail: detail('subscribedMonths', { tier, months }) }

  return { detail: detail('subscribed', { tier }) }
}

const KINDS: Record<string, ActivityKind> = {
  'channel.follow': { icon: UserAdd, label: 'channel_follow' },
  'channel.subscribe': { icon: Star, label: 'channel_subscribe', describe: subscribed },
  'channel.subscription.message': {
    icon: Star,
    label: 'channel_subscription_message',
    describe: subscribed,
  },
  'channel.subscription.gift': {
    icon: GiftCard,
    label: 'channel_subscription_gift',
    describe: (data: ActivityData) => {
      const tier = tierOf(data.tier)
      const count = pickCount(data, 'giftCount', 'count')

      if (tier === null || count === null)
        return null

      return { detail: detail('gifted', { count, tier }) }
    },
  },
  'channel.subscription.gift.received': {
    icon: GiftCard,
    label: 'channel_subscription_gift_received',
  },
  'channel.subscription.end': {
    icon: HeartBroken,
    label: 'channel_subscription_end',
    describe: (data: ActivityData) => {
      const tier = tierOf(data.tier)

      if (tier === null)
        return null

      return { detail: detail(data.isGift === true
        ? 'giftSubEnded'
        : 'subEnded', { tier }) }
    },
  },
  'channel.cheer': {
    icon: Diamond,
    label: 'channel_cheer',
    describe: (data: ActivityData) => {
      const count = pickCount(data, 'bits')

      return count === null
        ? null
        : { detail: detail('cheered', { count }) }
    },
  },
  'channel.raid': {
    icon: SwordsCrossing,
    label: 'channel_raid',
    describe: (data: ActivityData) => {
      const count = pickCount(data, 'viewerCount', 'viewers')

      return count === null
        ? null
        : { detail: detail('raidedWith', { count }) }
    },
  },
  'channel.raid.out': {
    icon: PaperPlaneSend,
    label: 'channel_raid_out',
    describe: (data: ActivityData) => {
      const name = pickText(data, 'user', 'targetDisplayName', 'toDisplayName')
      const count = pickCount(data, 'viewerCount', 'viewers')

      if (name === null || count === null)
        return null

      return { title: detail('raidedTarget', { name }), detail: detail('wentAlong', { count }) }
    },
  },
  'channel.raid.start': { icon: PaperPlaneSend, label: 'channel_raid_start' },
  'channel.channel_points_custom_reward_redemption.add': {
    icon: Ticket,
    label: 'channel_redemption',
    describe: (data: ActivityData) => {
      const reward = pickText(data, 'rewardTitle', 'reward')

      return reward === null
        ? null
        : { detail: detail('redeemed', { reward }) }
    },
  },
  'channel.ban': { icon: Hammer, label: 'channel_ban' },
  'channel.unban': { icon: UserRemove, label: 'channel_unban' },
  'channel.timeout': {
    icon: Timer,
    label: 'channel_timeout',
    describe: (data: ActivityData) => {
      const seconds = pickCount(data, 'durationSeconds')

      return seconds === null
        ? null
        : { detail: detail('timedOut', { minutes: Math.max(1, Math.round(seconds / 60)) }) }
    },
  },
  'channel.moderator.add': { icon: Moderator, label: 'channel_moderator_add' },
  'channel.moderator.remove': { icon: UserRemove, label: 'channel_moderator_remove' },
  'channel.hype_train.begin': { icon: Rocket, label: 'channel_hype_train_begin' },
  'channel.hype_train.end': { icon: Rocket, label: 'channel_hype_train_end' },
  'channel.poll.begin': { icon: PresentationChart, label: 'channel_poll_begin' },
  'channel.poll.end': { icon: PresentationChart, label: 'channel_poll_end' },
  'channel.prediction.begin': { icon: Trophy, label: 'channel_prediction_begin' },
  'channel.prediction.lock': { icon: Trophy, label: 'channel_prediction_lock' },
  'channel.prediction.end': { icon: Trophy, label: 'channel_prediction_end' },
  'channel.ad_break.begin': { icon: Timer, label: 'channel_ad_break_begin' },
  'engagement.first_time_chatter': { icon: ChatBubble, label: 'engagement_first_time_chatter' },
  'engagement.returning_chatter': { icon: Wave, label: 'engagement_returning_chatter' },
  'engagement.watch_streak': { icon: Fire, label: 'engagement_watch_streak' },
  'engagement.modiversary': { icon: BirthdayCake, label: 'engagement_modiversary' },
  'bot.shoutout.pending': { icon: Megaphone, label: 'bot_shoutout_pending' },
  'bot.shoutout.auto-pending': { icon: Megaphone, label: 'bot_shoutout_auto_pending' },
  'bot.shoutout.native-attempt': { icon: Megaphone, label: 'bot_shoutout_native_attempt' },
  'bot.shoutout.sent': { icon: Megaphone, label: 'bot_shoutout_sent' },
}

const OTHER: ActivityKind = { icon: Megaphone, label: 'other' }

export function describeActivity(event: ActivityEvent): ActivityRowView {
  const kind = KINDS[event.type] ?? OTHER
  const described: ReturnType<Describe> = kind.describe?.(parseActivityData(event.data)) ?? null

  return {
    icon: kind.icon,
    title: described?.title ?? null,
    detail: described?.detail ?? { key: TYPE + kind.label },
  }
}
