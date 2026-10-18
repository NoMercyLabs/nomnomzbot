<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One channel's billing: its invoices (refund after the amount is shown), its comps, and the tier and
// founders-badge grants. A comp reads the changed limits first and sends the counted number back.
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBillingGrants,
  useBillingInvoices,
  useBillingTiers,
  useGrantFounder,
  useGrantPreview,
  useGrantTier,
  useIssueGrant,
  useRefundInvoice,
  type BillingInvoice,
  type EntitlementGrantPreview,
} from '@/api/adminBilling';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';

const { t } = useI18n();

const typed = ref('');
const channelId = refDebounced(typed, 300);
const invoices = useBillingInvoices(channelId);
const grants = useBillingGrants(channelId);
const tiers = useBillingTiers();
const invoiceState = usePanelState(invoices, (list) =>
  list.length === 0);

const refund = useRefundInvoice();
const grantTier = useGrantTier();
const grantFounder = useGrantFounder();
const preview = useGrantPreview();
const issue = useIssueGrant();

const tierId = ref('');
const reason = ref('');
const expiresAt = ref('');
const refunding = ref<BillingInvoice | null>(null);
const action = ref<'tier' | 'founder' | 'comp' | null>(null);
const compForm = ref(false);
const counted = ref<EntitlementGrantPreview | null>(null);

const ready = computed<boolean>(() => channelId.value.trim() !== '');
const tierName = computed<string>(
  () => (tiers.data.value ?? []).find((tier) => tier.id === tierId.value)?.displayName ?? '',
);

const refundOpen = computed({
  get: () => refunding.value !== null,
  set: (open: boolean) => {
    if (!open) {
      refunding.value = null;
    }
  },
});
const confirmOpen = computed({
  get: () => action.value !== null,
  set: (open: boolean) => {
    if (!open) {
      action.value = null;
    }
  },
});

const amount = (invoice: BillingInvoice): string =>
  `${(Number(invoice.amountPaidCents) / 100).toFixed(2)} ${invoice.currency}`;

const confirmTitle = computed<string>(() => {
  if (action.value === 'founder')
    return t('dashboard.admin.billing.channel.founderTitle', { channel: channelId.value });

  return t('dashboard.admin.billing.channel.tierTitle', { channel: channelId.value, tier: tierName.value });
});
const consequences = computed<string[]>(() => {
  const count = Number(counted.value?.changedLimitCount ?? 0);
  const changed = action.value === 'comp'
    ? [t('dashboard.admin.billing.channel.changed', { count })]
    : [];

  return [...changed, ...(counted.value?.changedLimitKeys ?? [])];
});

async function confirmRefund(): Promise<void> {
  if (refunding.value === null) return;

  await refund.mutateAsync(refunding.value.id);
  refunding.value = null;
}

async function startComp(): Promise<void> {
  const target = { broadcasterId: channelId.value, tierId: tierId.value };

  counted.value = await preview.mutateAsync(target);
  compForm.value = false;
  action.value = 'comp';
}

async function confirmAction(): Promise<void> {
  if (action.value === 'tier') {
    await grantTier.mutateAsync({ broadcasterId: channelId.value, tierId: tierId.value });
  } else if (action.value === 'founder') {
    await grantFounder.mutateAsync(channelId.value);
  } else if (action.value === 'comp') {
    await issue.mutateAsync({
      broadcasterId: channelId.value,
      body: {
        tierId: tierId.value,
        reason: reason.value,
        expiresAt: new Date(expiresAt.value).toISOString(),
        confirmedChangedLimitCount: counted.value?.changedLimitCount ?? 0,
      },
    });
  }

  action.value = null;
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.billing.channel.title')"
    :state="ready ? invoiceState : 'empty'"
    :empty-text="ready ? t('dashboard.admin.billing.channel.noInvoices') : t('dashboard.admin.billing.channel.pick')"
    :error-text="t('dashboard.admin.billing.channel.error')"
    @retry="invoices.refetch()"
  >
    <template #actions>
      <Input
        v-model="typed"
        data-channel-id
        class="w-56"
        :aria-label="t('dashboard.admin.billing.channel.id')"
        :placeholder="t('dashboard.admin.billing.channel.id')"
      />
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="invoice in invoices.data.value ?? []" :key="invoice.id" data-invoice-row class="flex items-center gap-3 px-2 py-2">
        <span class="min-w-0 flex-1">{{ invoice.number }} <span class="text-muted-foreground">{{ invoice.status }}</span></span>
        <span class="tabular-nums">{{ amount(invoice) }}</span>
        <Button variant="outline" size="sm" data-invoice-refund @click="refunding = invoice">
          {{ t("dashboard.admin.billing.channel.refund") }}
        </Button>
      </li>
    </ul>
    <ul v-if="ready" class="grid divide-y divide-border border-t border-border text-sm">
      <li v-for="grant in grants.data.value ?? []" :key="grant.id" data-grant-row class="px-2 py-2">
        {{ grant.grantedTierKey }} <span class="text-muted-foreground">{{ grant.reason }}</span>
      </li>
    </ul>
    <template v-if="ready" #footer>
      <div class="flex flex-wrap items-center gap-2">
        <select v-model="tierId" data-grant-tier class="h-9 rounded-md border border-input bg-transparent px-3 text-sm">
          <option value="">{{ t("dashboard.admin.billing.channel.pickTier") }}</option>
          <option v-for="tier in tiers.data.value ?? []" :key="tier.id" :value="tier.id">
            {{ tier.displayName }}
          </option>
        </select>
        <Button variant="outline"
                size="sm"
                data-grant-tier-go
                :disabled="tierId === ''"
                @click="action = 'tier'"
        >
          {{ t("dashboard.admin.billing.channel.grantTier") }}
        </Button>
        <Button variant="outline"
                size="sm"
                data-grant-comp
                :disabled="tierId === ''"
                @click="compForm = true"
        >
          {{ t("dashboard.admin.billing.channel.comp") }}
        </Button>
        <Button variant="outline" size="sm" data-grant-founder @click="action = 'founder'">
          {{ t("dashboard.admin.billing.channel.founder") }}
        </Button>
      </div>
    </template>
  </PanelCard>
  <ConfirmDialog
    v-model:open="refundOpen"
    :title="t('dashboard.admin.billing.channel.refundTitle', { amount: refunding === null ? '' : amount(refunding) })"
    :action="t('dashboard.admin.billing.channel.refund')"
    :pending="refund.isPending.value"
    @confirm="confirmRefund"
  />
  <FormDialog
    v-model:open="compForm"
    :title="t('dashboard.admin.billing.channel.comp')"
    :action="t('dashboard.admin.billing.tiers.continue')"
    :pending="preview.isPending.value"
    @submit="startComp"
  >
    <div class="grid gap-3">
      <Input v-model="reason" :placeholder="t('dashboard.admin.billing.channel.reason')" />
      <Input v-model="expiresAt" type="date" :aria-label="t('dashboard.admin.billing.channel.expires')" />
    </div>
  </FormDialog>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="confirmTitle"
    :consequences="consequences"
    :action="t('dashboard.admin.billing.channel.confirm')"
    :pending="grantTier.isPending.value || grantFounder.isPending.value || issue.isPending.value"
    @confirm="confirmAction"
  />
</template>
