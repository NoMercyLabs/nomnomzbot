<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The redemption queue, newest first. Only a pending redemption can be fulfilled or refunded.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import Check from '~icons/nnz/check';
import Refund from '~icons/nnz/arrow-back-up';

import { useChannelAccess } from '@/api/access';
import { useRedemptionAction, useRedemptions, type Redemption } from '@/api/rewards';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { t, locale } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const query = useRedemptions(channelId);
const fulfill = useRedemptionAction(channelId, 'fulfill');
const refund = useRedemptionAction(channelId, 'refund');

const canManage = computed(() => access.can('reward:manage'));
const state = usePanelState(query, (rows: Redemption[]): boolean =>
  rows.length === 0);

const tones = {
  unfulfilled: 'live',
  fulfilled: 'ok',
  canceled: 'off',
} as const satisfies Record<string, StatusTone>;

function toneOf(status: string): StatusTone {
  return tones[status as keyof typeof tones] ?? 'off';
}

function statusLabel(status: string): string {
  return t(`dashboard.channelPoints.redemptions.status.${status}`, status);
}

function meta(item: Redemption): string {
  return t('dashboard.channelPoints.redemptions.meta', {
    count: Number(item.cost),
    when: when(item.redeemedAt),
  });
}

function when(redeemedAt: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(redeemedAt));
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.channelPoints.redemptions.title')"
    :state="state"
    :empty-text="$t('dashboard.channelPoints.redemptions.empty')"
    :error-text="$t('dashboard.channelPoints.redemptions.error')"
    @retry="query.refetch()"
  >
    <SummaryRow
      v-for="item in query.data.value"
      :key="item.redemptionId"
      :title="item.rewardTitle"
      :detail="item.userInput || undefined"
      data-redemption-item
    >
      <template #title>
        {{ item.rewardTitle }}
      </template>
      <template #end>
        <span class="flex flex-none flex-col items-end gap-0.5 text-xs text-muted-foreground">
          <UserName :user-id="item.userId" :name="item.userDisplayName" :color="item.userColor" />
          <span>{{ meta(item) }}</span>
        </span>
      </template>
      <template #trailing>
        <StatusBadge :tone="toneOf(item.status)" :label="statusLabel(item.status)" />
        <template v-if="canManage && item.status === 'unfulfilled'">
          <Button
            variant="outline"
            size="sm"
            :aria-label="$t('dashboard.channelPoints.redemptions.fulfillRow', { title: item.rewardTitle, name: item.userDisplayName })"
            data-redemption-fulfill
            @click="fulfill.mutate({ redemptionId: item.redemptionId })"
          >
            <Check aria-hidden="true" />
            {{ $t('dashboard.channelPoints.redemptions.fulfill') }}
          </Button>
          <Button
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.channelPoints.redemptions.refundRow', { title: item.rewardTitle, name: item.userDisplayName })"
            data-redemption-refund
            @click="refund.mutate({ redemptionId: item.redemptionId })"
          >
            <Refund class="size-5" aria-hidden="true" />
          </Button>
        </template>
      </template>
    </SummaryRow>
  </PanelCard>
</template>
