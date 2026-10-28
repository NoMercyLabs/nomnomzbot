<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The live quota overrides of one tenant: add or change one (key, value, reason, optional expiry), clear one.
// Clearing reverts that key to normal tier resolution.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import Cross from '~icons/nnz/cross';

import {
  useClearTenantLimit,
  useSetTenantLimit,
  useTenantLimits,
} from '@/api/adminTenants';
import FormDialog from '@/components/layout/FormDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { useTime } from '@/composables/useTime';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

const props = defineProps<{ tenantId: string }>();

const { t } = useI18n();
const time = useTime();
const { can } = usePlatformCan();

const limits = useTenantLimits(() => props.tenantId);
const save = useSetTenantLimit();
const clear = useClearTenantLimit();
const state = usePanelState(limits, (rows) =>
  rows.length === 0);

const open = ref(false);
const key = ref('');
const value = ref('');
const reason = ref('');
const expires = ref('');
const error = ref('');

function begin(): void {
  key.value = '';
  value.value = '';
  reason.value = '';
  expires.value = '';
  error.value = '';
  open.value = true;
}

function submit(): void {
  const raw = String(value.value).trim();
  const number = Number(raw);

  if (key.value.trim() === '' || raw === '' || !Number.isInteger(number)) {
    error.value = t('dashboard.admin.tenants.limits.invalid');

    return;
  }

  save.mutate(
    {
      broadcasterId: props.tenantId,
      limitKey: key.value.trim(),
      limitValue: number,
      reason: reason.value.trim(),
      expiresAt: expires.value === ''
        ? null
        : new Date(expires.value).toISOString(),
    },
    {
      onSuccess: () => {
        open.value = false;
      },
      onError: (cause: Error) => {
        error.value = cause.message;
      },
    },
  );
}

function expiry(at: string | null | undefined): string {
  return at
    ? time.date(at)
    : t('dashboard.admin.tenants.limits.noExpiry');
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.tenants.limits.title')"
    :description="t('dashboard.admin.tenants.limits.description')"
    :state="state"
    :empty-text="t('dashboard.admin.tenants.limits.empty')"
    :error-text="t('dashboard.admin.tenants.limits.error')"
    @retry="limits.refetch()"
  >
    <template #actions>
      <Button v-if="can('tenant:quota:manage')" variant="outline" size="sm" @click="begin">
        {{ t('dashboard.admin.tenants.limits.add') }}
      </Button>
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="row in limits.data.value" :key="row.id" data-limit-row class="flex items-center gap-3 px-2 py-2">
        <div class="min-w-0 flex-1">
          <span class="font-medium">{{ row.limitKey }}</span>
          <span class="ml-2 tabular-nums">{{ row.limitValue }}</span>
          <p class="text-muted-foreground">{{ row.reason }} {{ expiry(row.expiresAt) }}</p>
        </div>
        <Button
          v-if="can('tenant:quota:manage')"
          variant="ghost"
          size="icon"
          :data-clear-limit="row.limitKey"
          :aria-label="t('dashboard.admin.tenants.limits.clear', { key: row.limitKey })"
          :disabled="clear.isPending.value"
          @click="clear.mutate({ broadcasterId: tenantId, limitKey: row.limitKey })"
        >
          <Cross class="size-5" aria-hidden="true" />
        </Button>
      </li>
    </ul>
  </PanelCard>
  <FormDialog
    v-model:open="open"
    :title="t('dashboard.admin.tenants.limits.dialogTitle')"
    :description="t('dashboard.admin.tenants.limits.dialogDescription')"
    :action="t('dashboard.admin.tenants.limits.save')"
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <div class="grid gap-2">
      <Label for="limit-key">{{ t('dashboard.admin.tenants.limits.key') }}</Label>
      <Input id="limit-key" v-model="key" data-limit-key />
    </div>
    <div class="grid gap-2">
      <Label for="limit-value">{{ t('dashboard.admin.tenants.limits.value') }}</Label>
      <Input id="limit-value"
             v-model="value"
             data-limit-value
             type="number"
             inputmode="numeric"
      />
    </div>
    <div class="grid gap-2">
      <Label for="limit-reason">{{ t('dashboard.admin.tenants.limits.reason') }}</Label>
      <Input id="limit-reason" v-model="reason" data-limit-reason />
    </div>
    <div class="grid gap-2">
      <Label for="limit-expires">{{ t('dashboard.admin.tenants.limits.expires') }}</Label>
      <Input id="limit-expires" v-model="expires" type="datetime-local" />
    </div>
  </FormDialog>
</template>
