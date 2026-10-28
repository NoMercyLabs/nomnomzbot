<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed, onBeforeUnmount } from 'vue';
import { useI18n } from 'vue-i18n';

import { useResolveQueueItem, useReviewQueue, type ReviewQueueItem } from '@/api/home';
import { useShieldMode } from '@/api/shield';
import InlineFlow from '@/components/layout/InlineFlow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { DEFAULT_UNDO_MS } from '@/composables/useInlineFlow';
import { usePanelState } from '@/composables/usePanelState';

import { SHRINK_WHEN_EMPTY } from './shrinkWhenEmpty';

defineProps<{ live: boolean }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const queueQuery = useReviewQueue(channelId);
const { data: shieldOn } = useShieldMode(channelId);
const resolve = useResolveQueueItem(channelId);

const queue = computed<ReviewQueueItem[]>(() => queueQuery.data.value ?? []);
const queueState = usePanelState(queueQuery, (items: ReviewQueueItem[]) =>
  items.length === 0);

function sender(item: ReviewQueueItem): string {
  return item.targetUsernameSnapshot ?? t('dashboard.home.queue.unknownSender');
}

function allow(item: ReviewQueueItem): void {
  resolve.mutate({ queueItemId: item.id, action: 'approve' });
}

// A deny waits out the undo time before it reaches Twitch, so Undo only has to cancel the timer.
const pendingDenies = new Map();

function sendDeny(queueItemId: string): void {
  pendingDenies.delete(queueItemId);
  resolve.mutate({ queueItemId, action: 'deny' });
}

function holdDeny(queueItemId: string): void {
  pendingDenies.set(
    queueItemId,
    setTimeout(() => sendDeny(queueItemId), DEFAULT_UNDO_MS),
  );
}

function cancelDeny(queueItemId: string): void {
  clearTimeout(pendingDenies.get(queueItemId));
  pendingDenies.delete(queueItemId);
}

// Leaving the page does not take back a deny the person already made.
onBeforeUnmount(() => {
  [...pendingDenies.keys()].forEach((queueItemId: string) => {
    clearTimeout(pendingDenies.get(queueItemId));
    sendDeny(queueItemId);
  });
});
</script>

<template>
  <PanelCard
    :class="SHRINK_WHEN_EMPTY"
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
      <StatusBadge v-if="shieldOn && can('moderation:shieldmode:read')" tone="live" :label="t('dashboard.home.queue.shieldOn')" />
      <span class="text-sm text-muted-foreground">
        {{ t('dashboard.home.queue.waiting', { count: queue.length }) }}
      </span>
    </template>
    <RowGroup>
      <div v-for="item in queue" :key="item.id" data-queue-item>
        <SummaryRow :title="sender(item)" :detail="item.messageContentSnapshot ?? ''">
          <template v-if="can('moderation:queue:resolve')" #trailing>
            <div data-queue-actions class="flex items-center gap-2">
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
