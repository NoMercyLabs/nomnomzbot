// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useLocalStorage, type RemovableRef } from '@vueuse/core'
import { computed, type ComputedRef } from 'vue'

import { useEnterModeratedChannel, useModeratedChannels, useMyChannels } from '@/api/channels'

export type ChannelChoice = {
  login: string
  displayName: string
  profileImageUrl: string | null
  // Null when the bot has no live state for the channel (a Twitch channel the person moderates, not set up here yet).
  isLive: boolean | null
  kind: 'own' | 'moderated'
  // Null until the channel is set up in this bot; choosing it sets it up in moderator mode first.
  channelId: string | null
  twitchId: string | null
}

export type CurrentChannel = {
  channels: ComputedRef<ChannelChoice[]>
  current: ComputedRef<ChannelChoice | null>
  channelId: ComputedRef<string | null>
  isPending: ComputedRef<boolean>
  choose: (choice: ChannelChoice) => Promise<void>
}

// The channel being managed. It outlives a reload and is shared by every part of the frame.
const chosenChannelId: RemovableRef<string> = useLocalStorage<string>('nnz.channel', '')

export function useCurrentChannel(): CurrentChannel {
  const mine = useMyChannels()
  const moderated = useModeratedChannels()
  const enter = useEnterModeratedChannel()

  const channels = computed<ChannelChoice[]>(() => {
    const own: ChannelChoice[] = (mine.data.value ?? []).map((row) => ({
      login: row.login,
      displayName: row.displayName,
      profileImageUrl: row.profileImageUrl,
      isLive: row.isLive,
      kind: row.role === 'broadcaster' ? 'own' : 'moderated',
      channelId: row.id,
      twitchId: null,
    }))
    const known: Set<string> = new Set(own.map((row) => row.login.toLowerCase()))
    const notSetUp: ChannelChoice[] = (moderated.data.value ?? [])
      .filter((row) => !known.has(row.login.toLowerCase()))
      .map((row) => ({
        login: row.login,
        displayName: row.displayName,
        profileImageUrl: null,
        isLive: null,
        kind: 'moderated',
        channelId: null,
        twitchId: row.id,
      }))

    return [...own, ...notSetUp]
  })

  const current = computed<ChannelChoice | null>(
    () =>
      channels.value.find(
        (row) => row.channelId !== null && row.channelId === chosenChannelId.value,
      ) ??
      channels.value.find((row) => row.kind === 'own') ??
      channels.value.find((row) => row.channelId !== null) ??
      null,
  )

  async function choose(choice: ChannelChoice): Promise<void> {
    if (choice.channelId) {
      chosenChannelId.value = choice.channelId

      return
    }

    if (!choice.twitchId) return
    const entered = await enter.mutateAsync(choice.twitchId)

    if (entered) chosenChannelId.value = entered.id
  }

  return {
    channels,
    current,
    channelId: computed(() => current.value?.channelId ?? null),
    isPending: computed(() => enter.isPending.value),
    choose,
  }
}
