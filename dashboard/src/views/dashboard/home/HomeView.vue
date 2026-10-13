<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter, type Router } from 'vue-router'

import { useCurrentUser } from '@/api/auth'
import { useHomeStats, type HomeStats } from '@/api/home'
import BoardCell from '@/components/layout/BoardCell.vue'
import BoardGrid from '@/components/layout/BoardGrid.vue'
import { formatCountdown } from '@/components/layout/formatCountdown'
import PageHeader from '@/components/layout/PageHeader.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { useProviderName } from '@/composables/useProviderName'

import ConnectionsPanel from './components/ConnectionsPanel.vue'
import HappeningPanel from './components/HappeningPanel.vue'
import NeedsAttentionPanel from './components/NeedsAttentionPanel.vue'
import QuickActionsPanel from './components/QuickActionsPanel.vue'
import ReviewQueuePanel from './components/ReviewQueuePanel.vue'

const { t } = useI18n()
const router: Router = useRouter()
const { channelId } = useCurrentChannel()
const { data: user } = useCurrentUser()
const platformName = useProviderName()

const statsQuery = useHomeStats(channelId)

const stats: ComputedRef<HomeStats | null> = computed(() => statsQuery.data.value ?? null)
const live: ComputedRef<boolean> = computed(() => stats.value?.isLive === true)

const title: ComputedRef<string> = computed(() =>
  user.value
    ? t('dashboard.home.greeting', { name: user.value.displayName })
    : t('dashboard.home.title'),
)

const statusLabel: ComputedRef<string> = computed(() =>
  live.value
    ? t('dashboard.home.live', {
        platforms: (stats.value?.platformsLive ?? []).map(platformName).join(', '),
      })
    : t('dashboard.home.offline'),
)

const statsLine: ComputedRef<string> = computed(() => {
  const current: HomeStats | null = stats.value
  if (current === null) return ''
  if (current.isLive)
    return t('dashboard.home.liveLine', {
      uptime: formatCountdown(Number(current.uptime ?? 0)),
      viewers: current.viewerCount,
      followers: current.followerCount,
    })
  return t('dashboard.home.offlineLine', {
    followers: current.followerCount,
    subscribers: current.subscriberCount,
  })
})

// The one primary action follows the stream: chat while live, the stream setup while offline.
const primaryRoute: ComputedRef<{ name: string; label: string } | null> = computed(() => {
  const next: { name: string; label: string } = live.value
    ? { name: 'dashboard-chat', label: t('dashboard.home.openChat') }
    : { name: 'dashboard-stream', label: t('dashboard.home.editStream') }
  return router.hasRoute(next.name) ? next : null
})
</script>

<template>
  <PageHeader :title="title">
    <template #status>
      <StatusBadge :tone="live ? 'ok' : 'off'" :label="statusLabel" />
    </template>
    <template v-if="statsLine" #meta>{{ statsLine }}</template>
    <template v-if="primaryRoute" #actions>
      <Button as-child>
        <RouterLink :to="{ name: primaryRoute.name }">{{ primaryRoute.label }}</RouterLink>
      </Button>
    </template>
  </PageHeader>

  <BoardGrid :rows="2">
    <BoardCell :span="5">
      <ReviewQueuePanel :live="live" />
    </BoardCell>

    <BoardCell :span="4">
      <NeedsAttentionPanel />
    </BoardCell>

    <BoardCell :span="3" :rows="2">
      <ConnectionsPanel />
    </BoardCell>

    <BoardCell :span="5">
      <QuickActionsPanel />
    </BoardCell>

    <BoardCell :span="4">
      <HappeningPanel :live="live" />
    </BoardCell>
  </BoardGrid>
</template>
