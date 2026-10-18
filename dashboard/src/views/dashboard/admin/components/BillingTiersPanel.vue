<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Tiers: edit a tier after the server counts the channels the edit reaches, or author a new one.
// Saving always reads the preview first and sends the counted number back, so a stale edit is refused.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBillingTiers,
  useCreateTier,
  useSaveTier,
  useTierPreview,
  type BillingTier,
  type TierChangePreview,
} from '@/api/adminBilling';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { usePanelState } from '@/composables/usePanelState';

const { t } = useI18n();

const tiers = useBillingTiers();
const rows = computed<BillingTier[]>(() => tiers.data.value ?? []);
const state = usePanelState(tiers, (list) =>
  list.length === 0);

const preview = useTierPreview();
const save = useSaveTier();
const create = useCreateTier();

const formOpen = ref(false);
const confirmOpen = ref(false);
const editing = ref<BillingTier | null>(null);
const counted = ref<TierChangePreview | null>(null);

const key = ref('');
const displayName = ref('');
const priceCents = ref('0');
const limitsText = ref('');
const isPublic = ref(true);

const limitLines = (tier: BillingTier): string =>
  tier.limits.map((limit) => `${limit.limitKey}=${limit.limitValue}`).join('\n');

function parseLimits(): BillingTier['limits'] {
  return limitsText.value
    .split('\n')
    .map((line) => line.split('='))
    .filter((pair) => pair.length === 2 && pair[0]!.trim() !== '')
    .map(([limitKey, limitValue]) => ({
      limitKey: limitKey!.trim(),
      limitValue: Number(limitValue),
    }));
}

function openForm(tier: BillingTier | null): void {
  editing.value = tier;
  key.value = tier?.key ?? '';
  displayName.value = tier?.displayName ?? '';
  priceCents.value = String(tier?.priceCents ?? 0);

  limitsText.value = tier === null
    ? ''
    : limitLines(tier);

  isPublic.value = tier?.isPublic ?? true;
  formOpen.value = true;
}

const nextSort = computed<number>(() => {
  const orders = rows.value.map((tier) => Number(tier.sortOrder));

  return Math.max(0, ...orders) + 1;
});
const currency = computed<string>(() => rows.value[0]?.currency ?? 'USD');

async function submit(): Promise<void> {
  if (editing.value === null) {
    await create.mutateAsync({
      key: key.value,
      displayName: displayName.value,
      priceCents: Number(priceCents.value),
      currency: currency.value,
      allowsCustomBotName: false,
      prioritySupport: false,
      isPublic: isPublic.value,
      sortOrder: nextSort.value,
      limits: parseLimits(),
    });

    formOpen.value = false;

    return;
  }

  counted.value = await preview.mutateAsync(editing.value.id);
  confirmOpen.value = true;
}

async function confirmSave(): Promise<void> {
  const tier = editing.value;

  if (tier === null || counted.value === null) return;

  await save.mutateAsync({
    tierId: tier.id,
    body: {
      displayName: displayName.value,
      priceCents: Number(priceCents.value),
      currency: tier.currency,
      allowsCustomBotName: tier.allowsCustomBotName,
      prioritySupport: tier.prioritySupport,
      isPublic: isPublic.value,
      sortOrder: tier.sortOrder,
      limits: parseLimits(),
      confirmedAffectedTenantCount: counted.value.affectedTenantCount,
    },
  });

  confirmOpen.value = false;
  formOpen.value = false;
}

const consequences = computed<string[]>(() => {
  const count = Number(counted.value?.affectedTenantCount ?? 0);

  return [t('dashboard.admin.billing.tiers.affected', { count }), ...(counted.value?.sampleChannelNames ?? [])];
});
</script>

<template>
  <PanelCard
    tier="lead"
    :title="t('dashboard.admin.billing.tiers.title')"
    :state="state"
    :empty-text="t('dashboard.admin.billing.tiers.empty')"
    :error-text="t('dashboard.admin.billing.tiers.error')"
    @retry="tiers.refetch()"
  >
    <template #actions>
      <Button size="sm" data-tier-new @click="openForm(null)">
        {{ t("dashboard.admin.billing.tiers.new") }}
      </Button>
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="tier in rows" :key="tier.id" data-tier-row class="flex items-center gap-3 px-2 py-2">
        <div class="min-w-0 flex-1">
          <span class="font-medium">{{ tier.displayName }}</span>
          <span class="text-muted-foreground"> {{ tier.key }}</span>
          <Badge v-if="!tier.isPublic" variant="secondary" class="ml-2">
            {{ t("dashboard.admin.billing.tiers.private") }}
          </Badge>
        </div>
        <span class="text-muted-foreground tabular-nums">
          {{ (Number(tier.priceCents) / 100).toFixed(2) }} {{ tier.currency }}
        </span>
        <Button variant="outline" size="sm" data-tier-edit @click="openForm(tier)">
          {{ t("dashboard.admin.billing.tiers.edit") }}
        </Button>
      </li>
    </ul>
  </PanelCard>
  <FormDialog
    v-model:open="formOpen"
    data-tier-form
    :title="t(editing === null ? 'dashboard.admin.billing.tiers.new' : 'dashboard.admin.billing.tiers.edit')"
    :action="t('dashboard.admin.billing.tiers.continue')"
    :pending="preview.isPending.value || create.isPending.value"
    @submit="submit"
  >
    <div class="grid gap-3">
      <Input v-if="editing === null" v-model="key" :placeholder="t('dashboard.admin.billing.tiers.key')" />
      <Input v-model="displayName" :placeholder="t('dashboard.admin.billing.tiers.name')" />
      <Input v-model="priceCents" type="number" :aria-label="t('dashboard.admin.billing.tiers.priceCents')" />
      <Textarea v-model="limitsText" :placeholder="t('dashboard.admin.billing.tiers.limits')" />
      <label class="flex items-center gap-2 text-sm">
        <input v-model="isPublic" type="checkbox" />
        {{ t("dashboard.admin.billing.tiers.public") }}
      </label>
    </div>
  </FormDialog>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="t('dashboard.admin.billing.tiers.confirmTitle')"
    :consequences="consequences"
    :action="t('dashboard.admin.billing.tiers.save')"
    :pending="save.isPending.value"
    @confirm="confirmSave"
  />
</template>
