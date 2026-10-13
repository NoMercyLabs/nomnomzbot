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

import { useResolveQueueItem, useReviewQueue, type ReviewQueueItem } from '@/api/home'
import { useShieldMode } from '@/api/shield'
import InlineFlow from '@/components/layout/InlineFlow.vue'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { DEFAULT_UNDO_MS } from '@/composables/useInlineFlow'
import { usePanelState } from '@/composables/usePanelState'

defineProps<{ live: boolean }>()

const { t } = useI18n()
const { channelId } = useCurrentChannel()

const queueQuery = useReviewQueue(channelId)
const { data: shieldOn } = useShieldMode(channelId)
const resolve = useResolveQueueItem(channelId)

const queue: ComputedRef<ReviewQueueItem[]> = computed(() => queueQuery.data.value ?? [])
const queueState: ComputedRef<PanelState> = usePanelState(
  queueQuery,
  (items: ReviewQueueItem[]) => items.length === 0,
)

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
</script>

<template>
  <PanelCard
    :title="t('dashboard.home.queue.title')"
    :description="t('dashboard.home.queue.hint')"
    :tier="live ? 'lead' : 'default'"
    :state="queueState"
    :empty-text="
      live ? t('dashboard.home.queue.emptyLive') : t('dashboard.home.queue.emptyOffline')
    "
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
</template>
