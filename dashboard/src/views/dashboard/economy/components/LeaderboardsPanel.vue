<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The leaderboards the channel runs: what each one ranks and for which period.
import Pencil from '~icons/nnz/settings-fader';
import Trash from '~icons/nnz/trash';
import { computed, ref } from 'vue';

import {
  useDeleteLeaderboardConfig,
  useLeaderboardConfigs,
  type LeaderboardConfig,
} from '@/api/economy';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import LeaderboardDialog from './LeaderboardDialog.vue';

const adding = defineModel<boolean>('adding', { required: true });

const { channelId } = useCurrentChannel();
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
      <ul class="flex flex-col">
        <li
          v-for="row in rows"
          :key="row.id"
          data-leaderboard-row
          :data-leaderboard-name="row.id"
          class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <span class="min-w-0 flex-1">
            <span class="block text-sm font-medium">{{
              $t(`dashboard.economy.leaderboards.metrics.${row.metric}`)
            }}</span>
            <span class="block text-xs text-muted-foreground">{{
              $t('dashboard.economy.leaderboards.detail', {
                scope: $t(`dashboard.economy.leaderboards.scopes.${row.scope}`),
                period: $t(`dashboard.economy.leaderboards.periods.${row.period}`),
                top: Number(row.topN),
              })
            }}</span>
          </span>
          <StatusBadge
            v-if="!row.isPublic"
            tone="off"
            :label="$t('dashboard.economy.leaderboards.private')"
          />
          <Button
            data-leaderboard-edit
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.economy.leaderboards.edit')"
            @click="editing = row"
          >
            <Pencil aria-hidden="true" class="size-5" />
          </Button>
          <Button
            data-leaderboard-delete
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.economy.leaderboards.delete')"
            @click="deleting = row"
          >
            <Trash aria-hidden="true" class="size-5" />
          </Button>
        </li>
      </ul>
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
