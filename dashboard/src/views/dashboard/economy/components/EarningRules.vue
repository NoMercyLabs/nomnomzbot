<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How viewers earn the currency: one rule per source (chat, follows, raids, ...), each with its own
// amount and switch.
import Pencil from '~icons/nnz/settings-fader';
import Trash from '~icons/nnz/trash';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useDeleteEarningRule,
  useEarningRules,
  useSaveEarningRule,
  type EarningRule,
  type EarningRuleChange,
} from '@/api/economy';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import RuleDialog from './RuleDialog.vue';

const adding = defineModel<boolean>('adding', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const rules = useEarningRules(channelId);
const save = useSaveEarningRule(channelId);
const remove = useDeleteEarningRule(channelId);

const state = usePanelState(rules, (data: readonly EarningRule[]): boolean =>
  data.length === 0);
const rows = computed<readonly EarningRule[]>(() => rules.data.value ?? []);
const usedSources = computed<string[]>(() => rows.value.map((row: EarningRule) => row.source));

const editing = ref<EarningRule | null>(null);
const deleting = ref<EarningRule | null>(null);

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

function changeOf(row: EarningRule): EarningRuleChange {
  return {
    source: row.source,
    isEnabled: row.isEnabled,
    rate: row.rate,
    unitWindowSeconds: row.unitWindowSeconds,
    perWindowCap: row.perWindowCap,
    perStreamCap: row.perStreamCap,
    minRoleLevel: row.minRoleLevel,
    bonusConfig: row.bonusConfig,
  };
}

function toggle(row: EarningRule): void {
  save.mutate({ ...changeOf(row), isEnabled: !row.isEnabled });
}

function sourceName(source: string): string {
  return t(`dashboard.economy.sources.${source}`);
}

function detailOf(row: EarningRule): string {
  const rate = Number(row.rate);
  const seconds = Number(row.unitWindowSeconds ?? 0);
  const amount = t('dashboard.economy.rules.rate', { rate }, rate);

  return seconds > 0
    ? `${amount} · ${t('dashboard.economy.rules.window', { seconds }, seconds)}`
    : amount;
}

async function confirmDelete(): Promise<void> {
  const row = deleting.value;

  if (!row) return;

  await remove.mutateAsync(row.id);
  deleting.value = null;
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.economy.rules.title')"
    :description="$t('dashboard.economy.rules.description')"
    :state="state"
    :empty-text="$t('dashboard.economy.rules.empty')"
    :error-text="$t('dashboard.economy.rules.error')"
    class="max-w-2xl"
    @retry="rules.refetch()"
  >
    <ul class="flex flex-col">
      <li
        v-for="row in rows"
        :key="row.id"
        data-rule-row
        :data-rule-name="row.source"
        class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
      >
        <Switch
          :model-value="row.isEnabled"
          :disabled="!can('economy:earning-rules:write')"
          :aria-label="$t('dashboard.economy.rules.toggle', { source: sourceName(row.source) })"
          @update:model-value="toggle(row)"
        />
        <span class="min-w-0 flex-1">
          <span class="block text-sm font-medium">{{ sourceName(row.source) }}</span>
          <span class="block text-xs text-muted-foreground">{{ detailOf(row) }}</span>
        </span>
        <StatusBadge v-if="!row.isEnabled" tone="off" :label="$t('dashboard.economy.rules.off')" />
        <Button
          v-if="can('economy:earning-rules:write')"
          data-rule-edit
          type="button"
          variant="ghost"
          size="icon"
          :aria-label="$t('dashboard.economy.rules.edit')"
          @click="editing = row"
        >
          <Pencil aria-hidden="true" class="size-5" />
        </Button>
        <Button
          v-if="can('economy:earning-rules:delete')"
          data-rule-delete
          type="button"
          variant="ghost"
          size="icon"
          :aria-label="$t('dashboard.economy.rules.delete')"
          @click="deleting = row"
        >
          <Trash aria-hidden="true" class="size-5" />
        </Button>
      </li>
    </ul>
    <RuleDialog v-model:open="dialogOpen" :rule="editing" :used-sources="usedSources" />
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="$t('dashboard.economy.rules.confirmTitle')"
      :description="$t('dashboard.economy.rules.confirmBody')"
      :action="$t('dashboard.economy.rules.confirmAction')"
      :pending="remove.isPending.value"
      @confirm="confirmDelete"
    />
  </PanelCard>
</template>
