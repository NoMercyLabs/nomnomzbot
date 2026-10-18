<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The plans on offer. A channel without a paid plan goes to the payment page to start one; a channel with
// a paid plan switches in place, now or at the end of the period it has paid for.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import Crown from '~icons/nnz/crown';

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
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';
import { centsAsMoney } from '@/lib/number';

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

function limitsOf(tier: BillingTier): string {
  return tier.limits
    .map((limit) => `${limit.limitKey} ${Number(limit.limitValue) < 0
      ? '∞'
      : limit.limitValue}`)
    .join(' · ');
}

function includesOf(tier: BillingTier): string {
  const names: string[] = [];

  if (tier.allowsCustomBotName) {
    names.push(t('dashboard.settings.billing.tiers.customBotName'));
  }

  if (tier.prioritySupport) {
    names.push(t('dashboard.settings.billing.tiers.prioritySupport'));
  }

  return names.join(' · ');
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
      <RowGroup>
        <div
          v-for="tier in rows"
          :key="tier.key"
          :data-tier="tier.key"
          :data-current="tier.key === currentKey"
          class="flex flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
        >
          <Crown class="size-5 flex-none text-muted-foreground" />
          <div class="min-w-0 flex-1 basis-56">
            <div class="flex flex-wrap items-baseline gap-x-3">
              <span class="text-sm font-medium">{{ tier.displayName }}</span>
              <span data-tier-price class="text-sm text-muted-foreground">{{ priceOf(tier) }}</span>
            </div>
            <p v-if="tier.limits.length > 0" class="text-sm text-muted-foreground">{{ limitsOf(tier) }}</p>
            <p v-if="includesOf(tier)" class="text-sm text-muted-foreground">{{ includesOf(tier) }}</p>
          </div>
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
            :disabled="checkout.isPending.value || change.isPending.value"
            @click="choose(tier)"
          >
            {{ hasPaidPlan ? $t('dashboard.settings.billing.tiers.switch') : $t('dashboard.settings.billing.tiers.choose') }}
          </Button>
        </div>
      </RowGroup>
      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
  </PanelCard>
</template>
