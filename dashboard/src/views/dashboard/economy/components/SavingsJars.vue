<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's savings jars: shared pots viewers put currency into toward a goal. An open jar takes
// money; a closed jar keeps its balance.
import Trash from '~icons/nnz/trash';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useDeleteSavingsJar,
  useSavingsJars,
  useUpdateSavingsJar,
  type SavingsJar,
} from '@/api/economy';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import JarDialog from './JarDialog.vue';

const adding = defineModel<boolean>('adding', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const jars = useSavingsJars(channelId);
const update = useUpdateSavingsJar(channelId);
const remove = useDeleteSavingsJar(channelId);

const state = usePanelState(jars, (data: readonly SavingsJar[]): boolean =>
  data.length === 0);
const rows = computed<readonly SavingsJar[]>(() => jars.data.value ?? []);

const deleting = ref<SavingsJar | null>(null);
const confirmOpen = computed<boolean>({
  get: () => deleting.value !== null,
  set: (open: boolean): void => {
    if (!open) {
      deleting.value = null;
    }
  },
});

function toggle(row: SavingsJar): void {
  update.mutate({ jarId: row.id, change: { isOpen: !row.isOpen } });
}

function detailOf(row: SavingsJar): string {
  const balance = Number(row.balance);

  if (row.goalAmount === null || row.goalAmount === undefined)
    return t('dashboard.economy.jars.balance', { balance });

  return t('dashboard.economy.jars.progress', {
    balance,
    goal: Number(row.goalAmount),
  });
}

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
      :title="$t('dashboard.economy.jars.title')"
      :description="$t('dashboard.economy.jars.description')"
      :state="state"
      :empty-text="$t('dashboard.economy.jars.empty')"
      :error-text="$t('dashboard.economy.jars.error')"
      @retry="jars.refetch()"
    >
      <ul class="flex flex-col">
        <li
          v-for="row in rows"
          :key="row.id"
          data-jar-row
          :data-jar-name="row.name"
          class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <Switch
            :model-value="row.isOpen"
            :aria-label="$t('dashboard.economy.jars.toggle', { name: row.name })"
            @update:model-value="toggle(row)"
          />
          <span class="min-w-0 flex-1">
            <span class="block text-sm font-medium wrap-break-word">{{ row.name }}</span>
            <span class="block text-xs text-muted-foreground">{{ detailOf(row) }}</span>
          </span>
          <StatusBadge v-if="!row.isOpen" tone="off" :label="$t('dashboard.economy.jars.closed')" />
          <Button
            data-jar-delete
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.economy.jars.delete')"
            @click="deleting = row"
          >
            <Trash aria-hidden="true" class="size-5" />
          </Button>
        </li>
      </ul>
    </PanelCard>
    <PanelCard
      tier="quiet"
      :title="$t('dashboard.economy.jars.moneyTitle')"
      state="empty"
      :empty-text="$t('dashboard.economy.jars.notYet')"
    />
    <JarDialog v-model:open="adding" />
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="$t('dashboard.economy.jars.confirmTitle', { name: deleting?.name ?? '' })"
      :description="$t('dashboard.economy.jars.confirmBody')"
      :action="$t('dashboard.economy.jars.confirmAction')"
      :pending="remove.isPending.value"
      @confirm="confirmDelete"
    />
  </div>
</template>
