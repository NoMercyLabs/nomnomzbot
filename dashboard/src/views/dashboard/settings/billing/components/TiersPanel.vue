<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The plans on offer. A channel without a paid plan goes to the payment page to start one; a channel with
// a paid plan switches in place, now or at the end of the period it has paid for.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBillingTiers,
  useChangePlan,
  useStartCheckout,
  useSubscription,
  type BillingTier,
} from '@/api/billing';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';
import { centsAsMoney } from '@/lib/number';

import { formatAmount } from '../../resource-limits/limitUnits';
import { planLimitCell, shownLimitKeys, type PlanLimitKey } from '../planLimits';

const { t, locale } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const tiers = useBillingTiers(channelId);
const subscription = useSubscription(channelId);
const checkout = useStartCheckout(channelId);
const change = useChangePlan(channelId);

const atPeriodEnd = ref(true);
const failure = ref('');

const rows = computed(() =>
  [...(tiers.data.value ?? [])].sort((a, b) => Number(a.sortOrder) - Number(b.sortOrder)));
const currentKey = computed(() => subscription.data.value?.tierKey);
const hasPaidPlan = computed(() => {
  const current = rows.value.find((tier) => tier.key === currentKey.value);

  return current !== undefined && Number(current.priceCents) > 0;
});

const state = computed<PanelState>(() => {
  if (tiers.isError.value)
    return 'error';

  if (tiers.isPending.value)
    return 'loading';

  return rows.value.length === 0
    ? 'empty'
    : 'ready';
});

function priceOf(tier: BillingTier): string {
  if (Number(tier.priceCents) === 0)
    return t('dashboard.settings.billing.tiers.free');

  return t('dashboard.settings.billing.tiers.perMonth', {
    price: centsAsMoney(tier.priceCents, tier.currency, locale.value),
  });
}

const limitKeys = computed(() => shownLimitKeys(rows.value));

const features = [
  { key: 'customBotName', has: (tier: BillingTier) => tier.allowsCustomBotName },
  { key: 'prioritySupport', has: (tier: BillingTier) => tier.prioritySupport },
] as const satisfies readonly { key: string; has: (tier: BillingTier) => boolean }[];

function limitText(tier: BillingTier, key: PlanLimitKey): string {
  const cell = planLimitCell(tier, key, locale.value);

  if (cell.kind === 'missing' || cell.kind === 'none')
    return t('dashboard.settings.billing.tiers.notIncluded');

  if (cell.kind === 'unlimited')
    return t('dashboard.settings.billing.tiers.noLimit');

  if (cell.kind === 'characters')
    return t(
      'dashboard.settings.billing.tiers.characters',
      { n: formatAmount(cell.count, 'count', locale.value) },
      cell.count,
    );

  return cell.text;
}

function featureText(has: boolean): string {
  return has
    ? t('dashboard.settings.billing.tiers.included')
    : t('dashboard.settings.billing.tiers.notIncluded');
}

function offered(tier: BillingTier): boolean {
  if (tier.key === currentKey.value)
    return false;

  return hasPaidPlan.value || Number(tier.priceCents) > 0;
}

async function choose(tier: BillingTier): Promise<void> {
  failure.value = '';

  try {
    if (hasPaidPlan.value) {
      await change.mutateAsync({ tierKey: tier.key, atPeriodEnd: atPeriodEnd.value });
    } else {
      window.location.assign(await checkout.mutateAsync(tier.key));
    }
  } catch (error) {
    const key = hasPaidPlan.value
      ? 'dashboard.settings.billing.tiers.changeFailed'
      : 'dashboard.settings.billing.tiers.checkoutFailed';

    failure.value = `${t(key)} ${errorText(error, t)}`;
  }
}
</script>

<template>
  <PanelCard
    data-panel="tiers"
    tier="quiet"
    pad
    :title="$t('dashboard.settings.billing.tiers.title')"
    :description="$t('dashboard.settings.billing.tiers.description')"
    :state="state"
    :empty-text="$t('dashboard.settings.billing.tiers.empty')"
    :error-text="$t('dashboard.settings.billing.tiers.error')"
    @retry="tiers.refetch()"
  >
    <template v-if="hasPaidPlan && can('billing:manage')" #actions>
      <LabeledSwitch
        v-model="atPeriodEnd"
        data-field="at-period-end"
        :label="$t('dashboard.settings.billing.tiers.atPeriodEnd')"
        :help="$t('dashboard.settings.billing.tiers.atPeriodEndHelp')"
      />
    </template>
    <div class="grid gap-3">
      <div class="hidden md:block">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-1/4" />
              <TableHead
                v-for="tier in rows"
                :key="tier.key"
                :data-tier="tier.key"
                :data-current="tier.key === currentKey"
                class="h-auto py-3 align-top"
              >
                <div class="grid gap-1">
                  <span class="text-sm font-semibold">{{ tier.displayName }}</span>
                  <span data-tier-price class="text-sm font-normal text-muted-foreground">{{ priceOf(tier) }}</span>
                  <span
                    v-if="tier.key === currentKey"
                    data-current-marker
                    class="text-sm font-medium"
                  >
                    {{ $t('dashboard.settings.billing.tiers.current') }}
                  </span>
                  <Button
                    v-else-if="offered(tier) && can('billing:manage')"
                    data-action="choose"
                    size="sm"
                    variant="outline"
                    class="mt-1 w-fit"
                    :disabled="checkout.isPending.value || change.isPending.value"
                    @click="choose(tier)"
                  >
                    {{ hasPaidPlan ? $t('dashboard.settings.billing.tiers.switch') : $t('dashboard.settings.billing.tiers.choose') }}
                  </Button>
                </div>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="key in limitKeys" :key="key" :data-limit-key="key">
              <TableCell class="font-medium">{{ $t(`dashboard.settings.billing.tiers.limits.${key}`) }}</TableCell>
              <TableCell
                v-for="tier in rows"
                :key="tier.key"
                :data-limit-tier="tier.key"
                class="tabular-nums text-muted-foreground"
              >
                {{ limitText(tier, key) }}
              </TableCell>
            </TableRow>
            <TableRow v-for="feature in features" :key="feature.key" :data-feature="feature.key">
              <TableCell class="font-medium">{{ $t(`dashboard.settings.billing.tiers.${feature.key}`) }}</TableCell>
              <TableCell
                v-for="tier in rows"
                :key="tier.key"
                :data-feature-tier="tier.key"
                class="text-muted-foreground"
              >
                {{ featureText(feature.has(tier)) }}
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
      <div class="grid gap-3 md:hidden">
        <section
          v-for="tier in rows"
          :key="tier.key"
          :data-tier-card="tier.key"
          :data-current="tier.key === currentKey"
          class="grid gap-3 rounded-[8px] border p-3"
        >
          <div class="grid gap-1">
            <span class="text-sm font-semibold">{{ tier.displayName }}</span>
            <span data-card-price class="text-sm text-muted-foreground">{{ priceOf(tier) }}</span>
            <span v-if="tier.key === currentKey" class="text-sm font-medium">
              {{ $t('dashboard.settings.billing.tiers.current') }}
            </span>
            <Button
              v-else-if="offered(tier) && can('billing:manage')"
              data-action="choose"
              size="sm"
              variant="outline"
              class="mt-1 w-fit"
              :disabled="checkout.isPending.value || change.isPending.value"
              @click="choose(tier)"
            >
              {{ hasPaidPlan ? $t('dashboard.settings.billing.tiers.switch') : $t('dashboard.settings.billing.tiers.choose') }}
            </Button>
          </div>
          <dl class="grid gap-1 text-sm">
            <div
              v-for="key in limitKeys"
              :key="key"
              :data-card-limit="key"
              class="flex justify-between gap-3"
            >
              <dt class="font-medium">{{ $t(`dashboard.settings.billing.tiers.limits.${key}`) }}</dt>
              <dd class="text-right tabular-nums text-muted-foreground">{{ limitText(tier, key) }}</dd>
            </div>
            <div
              v-for="feature in features"
              :key="feature.key"
              :data-card-feature="feature.key"
              class="flex justify-between gap-3"
            >
              <dt class="font-medium">{{ $t(`dashboard.settings.billing.tiers.${feature.key}`) }}</dt>
              <dd class="text-right text-muted-foreground">{{ featureText(feature.has(tier)) }}</dd>
            </div>
          </dl>
        </section>
      </div>
      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
  </PanelCard>
</template>
