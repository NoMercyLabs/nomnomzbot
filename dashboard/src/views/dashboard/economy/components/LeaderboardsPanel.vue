<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The leaderboards the channel runs: what each one ranks and for which period.
import { computed, ref } from 'vue';

import {
  useDeleteLeaderboardConfig,
  useLeaderboardConfigs,
  type LeaderboardConfig,
} from '@/api/economy';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import LeaderboardDialog from './LeaderboardDialog.vue';

const adding = defineModel<boolean>('adding', { required: true });

const { channelId } = useCurrentChannel();
const { can } = useCan();
const configs = useLeaderboardConfigs(channelId);
const remove = useDeleteLeaderboardConfig(channelId);

const state = usePanelState(
  configs,
  (data: readonly LeaderboardConfig[]): boolean => data.length === 0,
);
const rows = computed<readonly LeaderboardConfig[]>(() => configs.data.value ?? []);

const editing = ref<LeaderboardConfig | null>(null);
const deleting = ref<LeaderboardConfig | null>(null);

const dialogOpen = computed<boolean>({
  get: () => adding.value || editing.value !== null,
  set: (open: boolean): void => {
    if (open) return;

    adding.value = false;
    editing.value = null;
  },
});
const confirmOpen = computed<boolean>({
  get: () => deleting.value !== null,
  set: (open: boolean): void => {
    if (!open) {
      deleting.value = null;
    }
  },
});

async function confirmDelete(): Promise<void> {
  const row = deleting.value;

  if (!row) return;

  await remove.mutateAsync(row.id);
  deleting.value = null;
}
</script>

<template>
  <div class="flex max-w-2xl flex-col gap-3">
    <PanelCard
      :title="$t('dashboard.economy.leaderboards.title')"
      :description="$t('dashboard.economy.leaderboards.description')"
      :state="state"
      :empty-text="$t('dashboard.economy.leaderboards.empty')"
      :error-text="$t('dashboard.economy.leaderboards.error')"
      @retry="configs.refetch()"
    >
      <RowGroup>
        <SummaryRow
          v-for="row in rows"
          :key="row.id"
          data-leaderboard-row
          :data-leaderboard-name="row.id"
          :title="$t(`dashboard.economy.leaderboards.metrics.${row.metric}`)"
          :detail="
            $t('dashboard.economy.leaderboards.detail', {
              scope: $t(`dashboard.economy.leaderboards.scopes.${row.scope}`),
              period: $t(`dashboard.economy.leaderboards.periods.${row.period}`),
              top: Number(row.topN),
            })
          "
        >
          <template #end>
            <StatusBadge
              v-if="!row.isPublic"
              tone="off"
              :label="$t('dashboard.economy.leaderboards.private')"
            />
            <RowEditDelete
              :editable="can('economy:leaderboards:config:write')"
              :deletable="can('economy:leaderboards:config:delete')"
              :edit-label="$t('dashboard.economy.leaderboards.edit')"
              :delete-label="$t('dashboard.economy.leaderboards.delete')"
              @edit="editing = row"
              @delete="deleting = row"
            />
          </template>
        </SummaryRow>
      </RowGroup>
    </PanelCard>
    <LeaderboardDialog v-model:open="dialogOpen" :config="editing" />
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="$t('dashboard.economy.leaderboards.confirmTitle')"
      :description="$t('dashboard.economy.leaderboards.confirmBody')"
      :action="$t('dashboard.economy.leaderboards.confirmAction')"
      :pending="remove.isPending.value"
      @confirm="confirmDelete"
    />
  </div>
</template>
