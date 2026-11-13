<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// How viewers earn the currency: one rule per source (chat, follows, raids, ...), each with its own
// amount and switch.
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
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
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
const usedSources = computed<string[]>(() => rows.value.map((row) => row.source));

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

function changeOf(row: EarningRule) {
  return {
    source: row.source,
    isEnabled: row.isEnabled,
    rate: row.rate,
    unitWindowSeconds: row.unitWindowSeconds,
    perWindowCap: row.perWindowCap,
    perStreamCap: row.perStreamCap,
    minRoleLevel: row.minRoleLevel,
    bonusConfig: row.bonusConfig,
  } as const satisfies EarningRuleChange;
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
    <RowGroup>
      <SummaryRow
        v-for="row in rows"
        :key="row.id"
        data-rule-row
        :data-rule-name="row.source"
        :title="sourceName(row.source)"
        :detail="detailOf(row)"
      >
        <template #end>
          <StatusBadge v-if="!row.isEnabled && can('economy:earning-rules:write')" tone="off" :label="$t('dashboard.economy.rules.off')" />
          <RowEditDelete
            :editable="can('economy:earning-rules:write')"
            :deletable="can('economy:earning-rules:delete')"
            :edit-label="$t('dashboard.economy.rules.edit')"
            :delete-label="$t('dashboard.economy.rules.delete')"
            @edit="editing = row"
            @delete="deleting = row"
          />
          <GatedSwitch
            action="economy:earning-rules:write"
            :model-value="row.isEnabled"
            :aria-label="$t('dashboard.economy.rules.toggle', { source: sourceName(row.source) })"
            @update:model-value="toggle(row)"
          />
        </template>
      </SummaryRow>
    </RowGroup>
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
