<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Priced units: what a metered unit costs per batch. Authoring a unit key that exists replaces its price.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAuthorPricedUnit, useBillingPricedUnits } from '@/api/adminBilling';
import FormDialog from '@/components/layout/FormDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

const { t } = useI18n();
const { can } = usePlatformCan();

const units = useBillingPricedUnits();
const state = usePanelState(units, (list) =>
  list.length === 0);
const author = useAuthorPricedUnit();

const open = ref(false);
const unitKey = ref('');
const currency = ref('USD');
const price = ref('0');
const batchSize = ref('1');

const rows = computed(() => units.data.value ?? []);

async function submit(): Promise<void> {
  await author.mutateAsync({
    unitKey: unitKey.value,
    currency: currency.value,
    priceMinorUnitsPerBatch: Number(price.value),
    batchSize: Number(batchSize.value),
  });

  open.value = false;
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.billing.units.title')"
    :state="state"
    :empty-text="t('dashboard.admin.billing.units.empty')"
    :error-text="t('dashboard.admin.billing.units.error')"
    @retry="units.refetch()"
  >
    <template #actions>
      <Button v-if="can('billing:write')"
              variant="outline"
              size="sm"
              data-unit-new
              @click="open = true"
      >
        {{ t("dashboard.admin.billing.units.new") }}
      </Button>
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="unit in rows" :key="unit.id" data-unit-row class="flex items-center gap-3 px-2 py-2">
        <span class="min-w-0 flex-1 font-medium">{{ unit.unitKey }}</span>
        <span class="text-muted-foreground tabular-nums">
          {{ t("dashboard.admin.billing.units.price", { price: unit.priceMinorUnitsPerBatch, currency: unit.currency, batch: unit.batchSize }) }}
        </span>
      </li>
    </ul>
  </PanelCard>
  <FormDialog
    v-model:open="open"
    data-unit-form
    :title="t('dashboard.admin.billing.units.new')"
    :action="t('dashboard.admin.billing.units.save')"
    :pending="author.isPending.value"
    @submit="submit"
  >
    <div class="grid gap-3">
      <Input v-model="unitKey" :placeholder="t('dashboard.admin.billing.units.key')" />
      <Input v-model="currency" :aria-label="t('dashboard.admin.billing.units.currency')" />
      <Input v-model="price" type="number" :aria-label="t('dashboard.admin.billing.units.priceInput')" />
      <Input v-model="batchSize" type="number" :aria-label="t('dashboard.admin.billing.units.batch')" />
    </div>
  </FormDialog>
</template>
