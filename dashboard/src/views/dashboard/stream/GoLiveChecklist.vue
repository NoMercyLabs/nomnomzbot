<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The checks before a stream starts. A check that cannot run says so; it never shows a guess.
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { useBotStatus } from '@/api/home'
import { useRewards, type Reward } from '@/api/rewards'
import { useShieldMode } from '@/api/shield'
import { useStreamInfo, type StreamInfo } from '@/api/stream'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

type Check = { key: string; tone: 'ok' | 'bad' | 'off'; label: string }

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const infoQuery = useStreamInfo(channelId)
const botQuery = useBotStatus(channelId)
const rewardsQuery = useRewards(channelId)
const shieldQuery = useShieldMode(channelId)

const state = usePanelState(infoQuery, (info: StreamInfo | null) => info === null)

function unchecked(key: string, what: string): Check {
  return {
    key,
    tone: 'off',
    label: t('dashboard.stream.checklist.couldNotCheck', {
      what: t(`dashboard.stream.checklist.what.${what}`),
    }),
  }
}

const checks: ComputedRef<Check[]> = computed(() => {
  const list: Check[] = []
  const info: StreamInfo | null = infoQuery.data.value ?? null

  if (info !== null) {
    const set: boolean = (info.title ?? '').trim() !== '' && (info.gameName ?? '').trim() !== ''

    list.push({
      key: 'title',
      tone: set
        ? 'ok'
        : 'bad',
      label: t(
        set
          ? 'dashboard.stream.checklist.titleSet'
          : 'dashboard.stream.checklist.titleMissing',
      ),
    })
    const tagCount = info.tags?.length ?? 0

    list.push({
      key: 'tags',
      tone: tagCount > 0
        ? 'ok'
        : 'off',
      label: t('dashboard.stream.checklist.tagsAdded', { count: tagCount }),
    })
  }

  if (botQuery.isError.value) {
    list.push(unchecked('bot', 'bot'))
  }
  else if (botQuery.data.value !== undefined) {
    const inChat = botQuery.data.value?.connected === true

    list.push({
      key: 'bot',
      tone: inChat
        ? 'ok'
        : 'bad',
      label: t(inChat
        ? 'dashboard.stream.checklist.botIn'
        : 'dashboard.stream.checklist.botOut'),
    })
  }

  if (rewardsQuery.isError.value) {
    list.push(unchecked('rewards', 'rewards'))
  }
  else if (rewardsQuery.data.value !== undefined) {
    const rewards = rewardsQuery.data.value
    const off = rewards.filter((reward: Reward) => !reward.isEnabled || reward.isPaused)
    const counts = { n: rewards.length - off.length, m: rewards.length } as const satisfies {
      n: number
      m: number
    }

    // A reward switched off is the streamer's choice, so it gets the neutral dot with its name.
    if (off.length === 0) {
      list.push({
        key: 'rewards',
        tone: 'ok',
        label: t('dashboard.stream.checklist.rewardsAllOn', counts),
      })
    }

    off.forEach((reward: Reward) =>
      list.push({
        key: `reward-${reward.id}`,
        tone: 'off',
        label: t('dashboard.stream.checklist.rewardOff', { ...counts, title: reward.title }),
      }),
    )
  }

  if (shieldQuery.isError.value) {
    list.push(unchecked('shield', 'shield'))
  }
  else if (shieldQuery.data.value !== undefined) {
    const shielded = shieldQuery.data.value

    list.push({
      key: 'shield',
      tone: shielded
        ? 'bad'
        : 'ok',
      label: t(
        shielded
          ? 'dashboard.stream.checklist.shieldOn'
          : 'dashboard.stream.checklist.shieldOff',
      ),
    })
  }

  return list
})
</script>

<template>
  <PanelCard
    :title="t('dashboard.stream.checklist.title')"
    tier="quiet"
    :state="state"
    :error-text="t('dashboard.stream.info.error')"
    @retry="infoQuery.refetch()"
  >
    <RowGroup>
      <div
        v-for="check in checks"
        :key="check.key"
        :data-check="check.key"
        class="flex min-h-11 items-center px-3 py-2"
      >
        <StatusBadge :tone="check.tone" :label="check.label" />
      </div>
    </RowGroup>
  </PanelCard>
</template>
