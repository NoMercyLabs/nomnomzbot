<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The delivery log of one outbound webhook, newest first, in a side panel: the event, the attempt, the answer
// code, how long it took and the error. A person who may write can send a delivery again.
import RotateRight from '~icons/nnz/rotate-right';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useOutboundDeliveries,
  useRetryOutboundDelivery,
  type OutboundWebhook,
} from '@/api/webhooks';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';
import { errorText } from '@/lib/errors';

const props = defineProps<{ endpoint: OutboundWebhook | null }>();

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const time = useTime();
const deliveries = useOutboundDeliveries(channelId, () =>
  props.endpoint?.id ?? null);
const retry = useRetryOutboundDelivery(channelId);
const state = usePanelState(deliveries, (rows) =>
  rows.length === 0);
const rows = computed(() => deliveries.data.value ?? []);
const failure = ref<string | undefined>(undefined);

async function again(deliveryId: number | string): Promise<void> {
  if (props.endpoint === null) return;

  failure.value = undefined;

  try {
    await retry.mutateAsync({ endpointId: props.endpoint.id, deliveryId: Number(deliveryId) });
    await deliveries.refetch();
  } catch (problem) {
    failure.value = errorText(problem, t);
  }
}
</script>

<template>
  <SidePanel
    v-model:open="open"
    drawer-only
    :label="$t('dashboard.settings.webhooks.deliveries.title')"
  >
    <PanelCard
      v-if="endpoint"
      flush
      data-panel="webhook-deliveries"
      :title="$t('dashboard.settings.webhooks.deliveries.title')"
      :description="endpoint.name"
      :state="state"
      :empty-text="$t('dashboard.settings.webhooks.deliveries.empty')"
      :error-text="$t('dashboard.settings.webhooks.deliveries.loadFailed')"
      @retry="deliveries.refetch()"
    >
      <RowGroup>
        <div
          v-for="delivery in rows"
          :key="String(delivery.id)"
          :data-delivery="String(delivery.id)"
          class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <div class="min-w-0 flex-1">
            <span class="flex flex-wrap items-center gap-2">
              <span class="text-sm font-medium wrap-break-word">{{ delivery.eventType }}</span>
              <Badge variant="outline">{{ delivery.status }}</Badge>
            </span>
            <span class="block text-sm text-muted-foreground">
              {{
                $t("dashboard.settings.webhooks.deliveries.line", {
                  code: delivery.responseCode ?? "-",
                  attempt: Number(delivery.attempt),
                  ms: delivery.durationMs ?? "-",
                  when: time.dateTime(delivery.createdAt),
                })
              }}
            </span>
            <span v-if="delivery.error" class="block text-sm text-destructive wrap-break-word">
              {{ delivery.error }}
            </span>
          </div>
          <Button
            v-if="can('webhooks:outbound:write')"
            variant="ghost"
            size="icon"
            data-delivery-retry
            :disabled="retry.isPending.value"
            :aria-label="$t('dashboard.settings.webhooks.deliveries.retry')"
            @click="again(delivery.id)"
          >
            <RotateRight class="size-5" />
          </Button>
        </div>
      </RowGroup>
      <p v-if="failure" role="alert" class="px-3 pt-2 text-sm text-destructive">{{ failure }}</p>
    </PanelCard>
  </SidePanel>
</template>
