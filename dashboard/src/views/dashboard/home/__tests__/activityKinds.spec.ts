// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest'

import type { ActivityEvent } from '@/api/home'

import {
  describeActivity,
  type ActivityRowText,
  type ActivityRowView,
} from '../components/activityKinds'
import { parseActivityData, tierOf } from '../components/activityData'

const event = (type: string, data: Record<string, unknown> | string | null): ActivityEvent => ({
  id: 'e',
  type,
  userId: 'u',
  username: 'someone',
  data: typeof data === 'object' && data !== null
    ? JSON.stringify(data)
    : data,
  timestamp: '2026-10-13T10:00:00Z',
})

const detailOf = (row: {
  detail: ActivityRowText
}): { key: string; params?: Record<string, unknown> } =>
  row.detail as { key: string; params?: Record<string, unknown> }

describe('parseActivityData', () => {
  it('reads a JSON object and gives an empty object for null, bad JSON and non-objects', () => {
    expect(parseActivityData('{"bits":50}')).toEqual({ bits: 50 })
    expect(parseActivityData(null)).toEqual({})
    expect(parseActivityData('not json')).toEqual({})
    expect(parseActivityData('[1,2]')).toEqual({})
    expect(parseActivityData('"x"')).toEqual({})
  })
})

describe('tierOf', () => {
  it('turns every tier spelling the server writes into one label', () => {
    expect(tierOf('1000')).toBe('1')
    expect(tierOf('Tier 2')).toBe('2')
    expect(tierOf('3')).toBe('3')
    expect(tierOf('Prime')).toBe('Prime')
    expect(tierOf(undefined)).toBeNull()
  })
})

describe('describeActivity', () => {
  it('describes a sub that ended: gift or not, with its tier', () => {
    const gift = describeActivity(event('channel.subscription.end', { tier: '1000', isGift: true }))

    expect(detailOf(gift)).toEqual({
      key: 'dashboard.home.activity.detail.giftSubEnded',
      params: { tier: '1' },
    })
    const plain = describeActivity(
      event('channel.subscription.end', { userDisplayName: 'night', tier: '2000' }),
    )

    expect(detailOf(plain).key).toBe('dashboard.home.activity.detail.subEnded')
  })

  it('names the raid target as the title and the party size as the detail on an outgoing raid', () => {
    const row = describeActivity(event('channel.raid.out', { user: 'JeroenLabs', viewers: '16' }))

    expect(row.title).toEqual({
      key: 'dashboard.home.activity.detail.raidedTarget',
      params: { name: 'JeroenLabs' },
    })
    expect(detailOf(row)).toEqual({
      key: 'dashboard.home.activity.detail.wentAlong',
      params: { count: 16 },
    })
  })

  it('reads the viewer count of an incoming raid from both writer shapes', () => {
    const projection = describeActivity(
      event('channel.raid', { fromDisplayName: 'CptTree', viewerCount: 6 }),
    )
    const handler = describeActivity(event('channel.raid', { user: 'CptTree', viewers: '6' }))

    expect(detailOf(projection).params).toEqual({ count: 6 })
    expect(detailOf(handler).params).toEqual({ count: 6 })
  })

  it('reads bits, gift counts and months from the data', () => {
    expect(detailOf(describeActivity(event('channel.cheer', { bits: 250 }))).params).toEqual({
      count: 250,
    })
    expect(
      detailOf(describeActivity(event('channel.subscription.gift', { giftCount: 5, tier: '1000' })))
        .params,
    ).toEqual({
      count: 5,
      tier: '1',
    })
    expect(
      detailOf(
        describeActivity(
          event('channel.subscription.message', { tier: '1000', cumulativeMonths: 7 }),
        ),
      ).params,
    ).toEqual({ tier: '1', months: 7 })
  })

  it('falls back to the plain label when the data lacks the detail', () => {
    const row = describeActivity(event('channel.cheer', null))

    expect(row.detail).toEqual({ key: 'dashboard.home.activity.type.channel_cheer' })
  })

  it('gives every known type an icon and never a dotted raw type for an unknown one', () => {
    const known: string[] = [
      'channel.follow',
      'channel.subscribe',
      'channel.subscription.message',
      'channel.subscription.gift',
      'channel.subscription.gift.received',
      'channel.subscription.end',
      'channel.cheer',
      'channel.raid',
      'channel.raid.out',
      'channel.raid.start',
      'channel.channel_points_custom_reward_redemption.add',
      'channel.ban',
      'channel.unban',
      'channel.timeout',
      'channel.moderator.add',
      'channel.moderator.remove',
      'channel.hype_train.begin',
      'channel.hype_train.end',
      'channel.poll.begin',
      'channel.poll.end',
      'channel.prediction.begin',
      'channel.prediction.lock',
      'channel.prediction.end',
      'channel.ad_break.begin',
      'engagement.first_time_chatter',
      'engagement.returning_chatter',
      'engagement.watch_streak',
      'engagement.modiversary',
      'bot.shoutout.pending',
      'bot.shoutout.auto-pending',
      'bot.shoutout.native-attempt',
      'bot.shoutout.sent',
    ]

    for (const type of known) {
      const row: ActivityRowView = describeActivity(event(type, null))

      expect({ type, hasIcon: Boolean(row.icon), key: row.detail.key }).toEqual({
        type,
        hasIcon: true,
        key: `dashboard.home.activity.type.${type.replace(/[.-]/g, '_').replace('channel_channel_points_custom_reward_redemption_add', 'channel_redemption')}`,
      })
    }

    const unknown = describeActivity(event('channel.something.new', null))

    expect(unknown.icon).toBeTruthy()
    expect(unknown.detail).toEqual({ key: 'dashboard.home.activity.type.other' })
  })
})
