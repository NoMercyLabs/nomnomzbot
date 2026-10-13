<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed, onBeforeUnmount, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter, type Router } from 'vue-router'

import { useActionRequired, type ActionRequiredItem } from '@/api/actionRequired'
import { useCurrentUser } from '@/api/auth'
import {
  useActivity,
  useBotStatus,
  useConnections,
  useHomeStats,
  useResolveQueueItem,
  useReviewQueue,
  type ActivityEvent,
  type HomeStats,
  type IntegrationStatus,
  type ReviewQueueItem,
} from '@/api/home'
import { useShieldMode } from '@/api/shield'
import BoardCell from '@/components/layout/BoardCell.vue'
import BoardGrid from '@/components/layout/BoardGrid.vue'
import { formatCountdown } from '@/components/layout/formatCountdown'
import InlineFlow from '@/components/layout/InlineFlow.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { DEFAULT_UNDO_MS } from '@/composables/useInlineFlow'
import { usePanelState } from '@/composables/usePanelState'

const { t, te, locale } = useI18n()
const router: Router = useRouter()
const { channelId } = useCurrentChannel()
const { data: user } = useCurrentUser()

const statsQuery = useHomeStats(channelId)
const queueQuery = useReviewQueue(channelId)
const attentionQuery = useActionRequired(channelId)
const connectionsQuery = useConnections(channelId)
const botQuery = useBotStatus(channelId)
const activityQuery = useActivity(channelId)
const { data: shieldOn } = useShieldMode(channelId)
const resolve = useResolveQueueItem(channelId)

const stats: ComputedRef<HomeStats | null> = computed(() => statsQuery.data.value ?? null)
const live: ComputedRef<boolean> = computed(() => stats.value?.isLive === true)
const queue: ComputedRef<ReviewQueueItem[]> = computed(() => queueQuery.data.value ?? [])

const queueState: ComputedRef<PanelState> = usePanelState(queueQuery, (items: ReviewQueueItem[]) => items.length === 0)
const attentionState: ComputedRef<PanelState> = usePanelState(
  attentionQuery,
  (items: ActionRequiredItem[]) => items.length === 0,
)
const connectionsState: ComputedRef<PanelState> = usePanelState(connectionsQuery)
const activityState: ComputedRef<PanelState> = usePanelState(activityQuery, (items: ActivityEvent[]) => items.length === 0)

const title: ComputedRef<string> = computed(() =>
  user.value ? t('dashboard.home.greeting', { name: user.value.displayName }) : t('dashboard.home.title'),
)

// A provider this client has no name for yet shows its id as the server sent it.
function platformName(id: string): string {
  const key: string = `common.provider.${id}`
  return te(key) ? t(key) : id
}

const statusLabel: ComputedRef<string> = computed(() =>
  live.value
    ? t('dashboard.home.live', { platforms: (stats.value?.platformsLive ?? []).map(platformName).join(', ') })
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
  return t('dashboard.home.offlineLine', { followers: current.followerCount, subscribers: current.subscriberCount })
})

// The one primary action follows the stream: chat while live, the stream setup while offline.
const primaryRoute: ComputedRef<{ name: string; label: string } | null> = computed(() => {
  const next: { name: string; label: string } = live.value
    ? { name: 'dashboard-chat', label: t('dashboard.home.openChat') }
    : { name: 'dashboard-stream', label: t('dashboard.home.editStream') }
  return router.hasRoute(next.name) ? next : null
})

function sender(item: ReviewQueueItem): string {
  return item.targetUsernameSnapshot ?? t('dashboard.home.queue.unknownSender')
}

function allow(item: ReviewQueueItem): void {
  resolve.mutate({ queueItemId: item.id, action: 'approve' })
}

// A deny waits out the undo time before it reaches Twitch, so Undo only has to cancel the timer.
const pendingDenies: Map<string, ReturnType<typeof setTimeout>> = new Map()

function sendDeny(queueItemId: string): void {
  pendingDenies.delete(queueItemId)
  resolve.mutate({ queueItemId, action: 'deny' })
}

function holdDeny(queueItemId: string): void {
  pendingDenies.set(
    queueItemId,
    setTimeout(() => sendDeny(queueItemId), DEFAULT_UNDO_MS),
  )
}

function cancelDeny(queueItemId: string): void {
  clearTimeout(pendingDenies.get(queueItemId))
  pendingDenies.delete(queueItemId)
}

// Leaving the page does not take back a deny the person already made.
onBeforeUnmount(() => {
  ;[...pendingDenies.keys()].forEach((queueItemId: string) => {
    clearTimeout(pendingDenies.get(queueItemId))
    sendDeny(queueItemId)
  })
})

// The server names each attention item by a words key; a key this client has no words for shows a plain fallback.
function words(key: string, fallback: string): string {
  return te(key) ? t(key) : t(fallback)
}

function inApp(link: string): boolean {
  return link.startsWith('/app')
}

function connectionTone(connection: IntegrationStatus): 'ok' | 'bad' | 'off' {
  if (!connection.connected) return 'off'
  return connection.needsReauth ? 'bad' : 'ok'
}

function connectionLabel(connection: IntegrationStatus): string {
  if (!connection.connected) return t('dashboard.home.connections.notConnected')
  return connection.needsReauth ? t('dashboard.home.connections.reauth') : t('dashboard.home.connections.connected')
}

function eventLabel(event: ActivityEvent): string {
  const key: string = `dashboard.home.activity.type.${event.type.replace('.', '_')}`
  return te(key) ? t(key) : t('dashboard.home.activity.type.other')
}

function eventTime(event: ActivityEvent): string {
  return new Intl.DateTimeFormat(locale.value, { hour: '2-digit', minute: '2-digit' }).format(new Date(event.timestamp))
}
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
      <PanelCard
        :title="t('dashboard.home.queue.title')"
        :description="t('dashboard.home.queue.hint')"
        :tier="live ? 'lead' : 'default'"
        :state="queueState"
        :empty-text="live ? t('dashboard.home.queue.emptyLive') : t('dashboard.home.queue.emptyOffline')"
        :error-text="t('dashboard.home.queue.error')"
        @retry="queueQuery.refetch()"
      >
        <template #actions>
          <StatusBadge v-if="shieldOn" tone="live" :label="t('dashboard.home.queue.shieldOn')" />
          <span class="text-sm text-muted-foreground">
            {{ t('dashboard.home.queue.waiting', { count: queue.length }) }}
          </span>
        </template>
        <RowGroup>
          <div v-for="item in queue" :key="item.id" data-queue-item>
          <SummaryRow :title="sender(item)" :detail="item.messageContentSnapshot ?? ''">
            <template #trailing>
              <div class="flex items-center gap-2">
                <Button variant="outline" size="sm" @click.stop="allow(item)">
                  {{ t('dashboard.home.queue.allow') }}
                </Button>
                <InlineFlow
                  kind="apply"
                  right
                  :label="t('dashboard.home.queue.review', { name: sender(item) })"
                  :result="t('dashboard.home.queue.denied')"
                  :action="() => holdDeny(item.id)"
                  :revert="() => cancelDeny(item.id)"
                >
                  <Button variant="destructive" size="sm">
                    {{ t('dashboard.home.queue.deny') }}
                  </Button>
                </InlineFlow>
              </div>
            </template>
          </SummaryRow>
          </div>
        </RowGroup>
      </PanelCard>
    </BoardCell>

    <BoardCell :span="4">
      <PanelCard
        :title="t('dashboard.home.attention.title')"
        :state="attentionState"
        :empty-text="t('dashboard.home.attention.empty')"
        :error-text="t('dashboard.home.attention.error')"
        @retry="attentionQuery.refetch()"
      >
        <RowGroup>
          <SummaryRow
            v-for="item in attentionQuery.data.value ?? []"
            :key="item.id"
            :title="words(item.titleKey, 'frame.inbox.unknownTitle')"
            :detail="words(item.messageKey, 'frame.inbox.unknownMessage')"
          >
            <template v-if="inApp(item.deepLinkRoute)" #trailing>
              <Button variant="outline" size="sm" as-child>
                <RouterLink :to="item.deepLinkRoute">{{ t('dashboard.home.attention.open') }}</RouterLink>
              </Button>
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
    </BoardCell>

    <BoardCell :span="3" :rows="2">
      <PanelCard
        :title="t('dashboard.home.connections.title')"
        :description="t('dashboard.home.connections.services')"
        tier="quiet"
        :state="connectionsState"
        :error-text="t('dashboard.home.connections.error')"
        @retry="connectionsQuery.refetch()"
      >
        <RowGroup>
          <SummaryRow
            :title="t('dashboard.home.connections.bot')"
            :detail="botQuery.data.value?.displayName ?? undefined"
          >
            <template #trailing>
              <StatusBadge
                :tone="botQuery.data.value?.connected ? 'ok' : 'off'"
                :label="
                  botQuery.data.value?.connected
                    ? t('dashboard.home.connections.connected')
                    : t('dashboard.home.connections.botOffline')
                "
              />
            </template>
          </SummaryRow>
          <SummaryRow
            v-for="connection in connectionsQuery.data.value ?? []"
            :key="connection.provider"
            :title="platformName(connection.provider)"
            :detail="connection.accountName ?? undefined"
          >
            <template #trailing>
              <StatusBadge :tone="connectionTone(connection)" :label="connectionLabel(connection)" />
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
    </BoardCell>

    <BoardCell :span="9">
      <PanelCard
        :title="live ? t('dashboard.home.activity.live') : t('dashboard.home.activity.offline')"
        :state="activityState"
        :empty-text="t('dashboard.home.activity.empty')"
        :error-text="t('dashboard.home.activity.error')"
        @retry="activityQuery.refetch()"
      >
        <RowGroup>
          <SummaryRow
            v-for="event in activityQuery.data.value ?? []"
            :key="event.id"
            :title="event.username ?? eventLabel(event)"
            :detail="event.username ? eventLabel(event) : undefined"
          >
            <template #trailing>
              <span class="text-sm text-muted-foreground tabular-nums">{{ eventTime(event) }}</span>
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
    </BoardCell>
  </BoardGrid>
</template>
