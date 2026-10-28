<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel's store: items viewers buy with the currency. Each row switches on or off, opens its
// editor, or is deleted after a confirm.
import Pencil from '~icons/nnz/settings-fader';
import Trash from '~icons/nnz/trash';
import { computed, ref } from 'vue';

import {
  useCatalogItems,
  useDeleteCatalogItem,
  useUpdateCatalogItem,
  type CatalogItem,
} from '@/api/economy';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import CatalogDialog from './CatalogDialog.vue';

const adding = defineModel<boolean>('adding', { required: true });

const { channelId } = useCurrentChannel();
const { can } = useCan();
const items = useCatalogItems(channelId);
const update = useUpdateCatalogItem(channelId);
const remove = useDeleteCatalogItem(channelId);

const state = usePanelState(items, (data: readonly CatalogItem[]): boolean =>
  data.length === 0);
const rows = computed<readonly CatalogItem[]>(() => items.data.value ?? []);

const editing = ref<CatalogItem | null>(null);
const deleting = ref<CatalogItem | null>(null);

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

function toggle(row: CatalogItem): void {
  update.mutate({ itemId: row.id, change: { isEnabled: !row.isEnabled } });
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
      :title="$t('dashboard.economy.catalog.title')"
      :description="$t('dashboard.economy.catalog.description')"
      :state="state"
      :empty-text="$t('dashboard.economy.catalog.empty')"
      :error-text="$t('dashboard.economy.catalog.error')"
      @retry="items.refetch()"
    >
      <ul class="flex flex-col">
        <li
          v-for="row in rows"
          :key="row.id"
          data-catalog-row
          :data-catalog-name="row.name"
          class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <span class="min-w-0 flex-1">
            <span class="block text-sm font-medium wrap-break-word">{{ row.name }}</span>
            <span class="block text-xs text-muted-foreground">{{
              $t('dashboard.economy.catalog.cost', { cost: Number(row.cost) })
            }}</span>
          </span>
          <Button
            v-if="can('economy:catalog:update')"
            data-catalog-edit
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.economy.catalog.edit')"
            @click="editing = row"
          >
            <Pencil aria-hidden="true" class="size-5" />
          </Button>
          <Button
            v-if="can('economy:catalog:delete')"
            data-catalog-delete
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.economy.catalog.delete')"
            @click="deleting = row"
          >
            <Trash aria-hidden="true" class="size-5" />
          </Button>
          <GatedSwitch
            action="economy:catalog:update"
            :model-value="row.isEnabled"
            :aria-label="$t('dashboard.economy.catalog.toggle', { name: row.name })"
            @update:model-value="toggle(row)"
          />
        </li>
      </ul>
    </PanelCard>
    <CatalogDialog v-model:open="dialogOpen" :item="editing" />
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="$t('dashboard.economy.catalog.confirmTitle', { name: deleting?.name ?? '' })"
      :description="$t('dashboard.economy.catalog.confirmBody')"
      :action="$t('dashboard.economy.catalog.confirmAction')"
      :pending="remove.isPending.value"
      @confirm="confirmDelete"
    />
  </div>
</template>
