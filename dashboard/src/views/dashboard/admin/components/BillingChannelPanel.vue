<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One channel's billing: its invoices (refund after the amount is shown), its gifts, and the tier and
// founders-badge grants. A gift gives any tier (a hidden one too) for N months or forever; it reads the
// changed limits first, names the end date it will store, and sends the counted number back. The tier and
// length fields are shared with the invite form (TierLengthFields).
import { refDebounced } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBillingGrants,
  useBillingInvoices,
  useEndGrant,
  useGrantFounder,
  useGrantPreview,
  useGrantTier,
  useIssueGrant,
  useRefundInvoice,
  type BillingInvoice,
  type EntitlementGrant,
  type EntitlementGrantPreview,
} from '@/api/adminBilling';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { merge } from '@/lib/array';
import { addMonths } from '@/lib/time';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { useTime } from '@/composables/useTime';

import TierLengthFields from './TierLengthFields.vue';
import { useTierLength } from './tierLength';

// A channel console passes its channel, which fixes the id; the Billing page leaves it out and types one.
const props = defineProps<{ channel?: string }>();

const { t } = useI18n();
const { can } = usePlatformCan();
const time = useTime();

const typed = ref(props.channel ?? '');
const channelId = refDebounced(typed, 300);
const invoices = useBillingInvoices(channelId);
const grants = useBillingGrants(channelId);
const {
  tierId: giftTierId,
  months,
  forever,
  tierChoices,
  monthCount,
  lengthValid,
  tierLabel,
  reset: resetLength,
} = useTierLength();
const invoiceState = usePanelState(invoices, (list) =>
  list.length === 0);

const refund = useRefundInvoice();
const grantTier = useGrantTier();
const grantFounder = useGrantFounder();
const preview = useGrantPreview();
const issue = useIssueGrant();
const endGrant = useEndGrant();

const tierId = ref('');
const reason = ref('');
const refunding = ref<BillingInvoice | null>(null);
const ending = ref<EntitlementGrant | null>(null);
const action = ref<'tier' | 'founder' | 'gift' | null>(null);
const giftForm = ref(false);
const counted = ref<EntitlementGrantPreview | null>(null);

const ready = computed<boolean>(() => channelId.value.trim() !== '');

// A channel with no invoices still shows its gifts, so an empty invoice list is a ready panel.
const panelState = computed(() => {
  if (!ready.value)
    return 'empty';

  return invoiceState.value === 'empty'
    ? 'ready'
    : invoiceState.value;
});
const noInvoices = computed<boolean>(() => (invoices.data.value ?? []).length === 0);
const noGifts = computed<boolean>(() => (grants.data.value ?? []).length === 0);

const giftReady = computed<boolean>(() =>
  giftTierId.value !== '' && reason.value.trim() !== '' && lengthValid.value);

const giftSummary = computed<string>(() => {
  const tier = tierLabel(giftTierId.value);

  if (forever.value)
    return t('dashboard.admin.billing.channel.giftForever', { tier });

  const end = addMonths(new Date(), monthCount.value);
  const date = time.date(end);

  return t('dashboard.admin.billing.channel.giftUntil', { tier, date });
});
const showSummary = computed<boolean>(() =>
  giftTierId.value !== '' && lengthValid.value);

function grantEnd(grant: EntitlementGrant): string {
  if (grant.expiresAt === null || grant.expiresAt === undefined)
    return t('dashboard.admin.billing.channel.forever');

  return t('dashboard.admin.billing.channel.until', { date: time.date(grant.expiresAt) });
}

const refundOpen = computed({
  get: () => refunding.value !== null,
  set: (open: boolean) => {
    if (!open) {
      refunding.value = null;
    }
  },
});
const endOpen = computed({
  get: () => ending.value !== null,
  set: (open: boolean) => {
    if (!open) {
      ending.value = null;
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

  const channel = channelId.value;

  if (action.value === 'gift')
    return t('dashboard.admin.billing.channel.giftTitle', { channel, tier: tierLabel(giftTierId.value) });

  return t('dashboard.admin.billing.channel.tierTitle', { channel, tier: tierLabel(tierId.value) });
});
const consequences = computed<string[]>(() => {
  if (action.value !== 'gift')
    return [];

  const count = Number(counted.value?.changedLimitCount ?? 0);
  const changed = [giftSummary.value, t('dashboard.admin.billing.channel.changed', { count })];

  return Array.from(merge(changed, counted.value?.changedLimitKeys ?? []));
});

async function confirmRefund(): Promise<void> {
  if (refunding.value === null) return;

  await refund.mutateAsync(refunding.value.id);
  refunding.value = null;
}

async function confirmEnd(): Promise<void> {
  if (ending.value === null) return;

  await endGrant.mutateAsync({ broadcasterId: channelId.value, grantId: ending.value.id });
  ending.value = null;
}

function openGift(): void {
  resetLength();
  reason.value = '';
  giftForm.value = true;
}

async function startGift(): Promise<void> {
  const target = { broadcasterId: channelId.value, tierId: giftTierId.value };

  counted.value = await preview.mutateAsync(target);
  giftForm.value = false;
  action.value = 'gift';
}

// A gift runs forever or for whole months; the server takes exactly one of the two.
function giftLength() {
  if (forever.value)
    return { forever: true };

  return { months: monthCount.value, forever: false };
}

async function confirmAction(): Promise<void> {
  if (action.value === 'tier') {
    await grantTier.mutateAsync({ broadcasterId: channelId.value, tierId: tierId.value });
  } else if (action.value === 'founder') {
    await grantFounder.mutateAsync(channelId.value);
  } else if (action.value === 'gift') {
    await issue.mutateAsync({
      broadcasterId: channelId.value,
      body: {
        tierId: giftTierId.value,
        reason: reason.value.trim(),
        ...giftLength(),
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
    :state="panelState"
    :empty-text="t('dashboard.admin.billing.channel.pick')"
    :error-text="t('dashboard.admin.billing.channel.error')"
    @retry="invoices.refetch()"
  >
    <template #actions>
      <Input
        v-if="channel === undefined"
        v-model="typed"
        data-channel-id
        class="w-56"
        :aria-label="t('dashboard.admin.billing.channel.id')"
        :placeholder="t('dashboard.admin.billing.channel.id')"
      />
    </template>
    <p v-if="noInvoices" data-no-invoices class="px-2 py-2 text-sm text-muted-foreground">
      {{ t("dashboard.admin.billing.channel.noInvoices") }}
    </p>
    <ul v-else class="grid divide-y divide-border text-sm">
      <li v-for="invoice in invoices.data.value ?? []" :key="invoice.id" data-invoice-row class="flex items-center gap-3 px-2 py-2">
        <span class="min-w-0 flex-1">{{ invoice.number }} <span class="text-muted-foreground">{{ invoice.status }}</span></span>
        <span class="tabular-nums">{{ amount(invoice) }}</span>
        <Button v-if="can('billing:refund')"
                variant="outline"
                size="sm"
                data-invoice-refund
                @click="refunding = invoice"
        >
          {{ t("dashboard.admin.billing.channel.refund") }}
        </Button>
      </li>
    </ul>
    <p v-if="noGifts" data-no-gifts class="border-t border-border px-2 py-2 text-sm text-muted-foreground">
      {{ t("dashboard.admin.billing.channel.noGifts") }}
    </p>
    <ul v-else class="grid divide-y divide-border border-t border-border text-sm">
      <li v-for="grant in grants.data.value ?? []"
          :key="grant.id"
          :data-grant-row="grant.id"
          class="flex items-center gap-3 px-2 py-2"
      >
        <span class="min-w-0 flex-1">
          {{ tierLabel(grant.grantedTierId) || grant.grantedTierKey }}
          <span class="text-muted-foreground">{{ grant.reason }}</span>
        </span>
        <span class="tabular-nums text-muted-foreground">{{ grantEnd(grant) }}</span>
        <Button v-if="can('billing:write')"
                variant="outline"
                size="sm"
                data-grant-end
                @click="ending = grant"
        >
          {{ t("dashboard.admin.billing.channel.endNow") }}
        </Button>
      </li>
    </ul>
    <template v-if="ready && can('billing:write')" #footer>
      <div class="flex flex-wrap items-center gap-2">
        <Button size="sm" data-grant-gift @click="openGift">
          {{ t("dashboard.admin.billing.channel.gift") }}
        </Button>
        <!-- The trigger fills its box, so the box sets the width. -->
        <div class="w-48">
          <OptionSelect
            v-model="tierId"
            data-grant-tier
            :options="tierChoices"
            :placeholder="t('dashboard.admin.billing.channel.pickTier')"
          />
        </div>
        <Button variant="outline"
                size="sm"
                data-grant-tier-go
                :disabled="tierId === ''"
                @click="action = 'tier'"
        >
          {{ t("dashboard.admin.billing.channel.grantTier") }}
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
  <ConfirmDialog
    v-model:open="endOpen"
    :title="t('dashboard.admin.billing.channel.endTitle', { tier: ending === null ? '' : tierLabel(ending.grantedTierId) })"
    :consequences="[t('dashboard.admin.billing.channel.endConsequence')]"
    :action="t('dashboard.admin.billing.channel.endNow')"
    :pending="endGrant.isPending.value"
    @confirm="confirmEnd"
  />
  <FormDialog
    v-if="can('billing:write')"
    v-model:open="giftForm"
    :title="t('dashboard.admin.billing.channel.gift')"
    :action="t('dashboard.admin.billing.tiers.continue')"
    :pending="preview.isPending.value"
    :disabled="!giftReady"
    @submit="startGift"
  >
    <TierLengthFields
      id="gift"
      v-model:tier-id="giftTierId"
      v-model:months="months"
      v-model:forever="forever"
      :choices="tierChoices"
    />
    <div class="grid gap-2">
      <Label for="gift-reason">{{ t('dashboard.admin.billing.channel.reason') }}</Label>
      <Input id="gift-reason" v-model="reason" data-gift-reason />
    </div>
    <p v-if="showSummary" data-gift-summary class="text-sm text-muted-foreground">
      {{ giftSummary }}
    </p>
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
