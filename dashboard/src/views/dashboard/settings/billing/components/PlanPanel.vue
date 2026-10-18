<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The plan this channel is on: its name, its state, its dates and what it includes. A plan that is set to
// end offers Keep my plan as the one main action; otherwise the payment page and the cancel stay quiet.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import CreditCard from '~icons/nnz/credit-card-front';

import { useCancelPlan, useOpenBillingPortal, useResumePlan, useSubscription } from '@/api/billing';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { lookup, merge } from '@/lib/array';
import { errorText } from '@/lib/errors';

const { t, te, d } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const subscription = useSubscription(channelId);
const resume = useResumePlan(channelId);
const portal = useOpenBillingPortal(channelId);
const cancel = useCancelPlan(channelId);

const cancelOpen = ref(false);
const cancelNow = ref(false);
const reason = ref('');
const failure = ref('');

const plan = computed(() => subscription.data.value);
const state = computed<PanelState>(() => {
  if (subscription.isError.value)
    return 'error';

  return subscription.isPending.value
    ? 'loading'
    : 'ready';
});

const tones = {
  active: 'ok',
  trialing: 'ok',
  past_due: 'bad',
  canceled: 'off',
  incomplete: 'off',
} as const satisfies Record<string, StatusTone>;

const tone = computed<StatusTone>(() => lookup(tones, plan.value?.status) ?? 'off');
const statusLabel = computed(() => {
  const key = `dashboard.settings.billing.status.${plan.value?.status ?? ''}`;

  return te(key)
    ? t(key)
    : plan.value?.status ?? '';
});

type DateRow = {
  id: string;
  label: string;
  value: string;
};

function dateRow(id: string, label: string, value: string | null | undefined): DateRow[] {
  if (value === undefined || value === null)
    return [];

  return [{
    id,
    label: t(label),
    value: d(new Date(value), { dateStyle: 'medium' }),
  }];
}

const dates = computed<readonly DateRow[]>(() => {
  const current = plan.value;

  if (current === undefined)
    return [];

  const periodLabel = current.cancelAtPeriodEnd
    ? 'dashboard.settings.billing.plan.endsOn'
    : 'dashboard.settings.billing.plan.renews';

  return merge(
    dateRow('period', periodLabel, current.currentPeriodEnd),
    dateRow('trial', 'dashboard.settings.billing.plan.trialEnds', current.trialEndsAt),
    dateRow('grace', 'dashboard.settings.billing.plan.graceEnds', current.gracePeriodEndsAt),
  );
});

const manageable = computed(
  () => can('billing:manage') && plan.value !== undefined && !plan.value.isInviteOnlyGrant,
);
const cancellable = computed(() => manageable.value && plan.value?.status !== 'canceled');

function yesNo(value: boolean): string {
  return value
    ? t('dashboard.settings.billing.plan.yes')
    : t('dashboard.settings.billing.plan.no');
}

async function keepPlan(): Promise<void> {
  failure.value = '';

  try {
    await resume.mutateAsync();
  } catch (error) {
    failure.value = `${t('dashboard.settings.billing.plan.resumeFailed')} ${errorText(error, t)}`;
  }
}

async function openPayment(): Promise<void> {
  failure.value = '';

  try {
    window.location.assign(await portal.mutateAsync());
  } catch (error) {
    failure.value = `${t('dashboard.settings.billing.plan.paymentFailed')} ${errorText(error, t)}`;
  }
}

async function confirmCancel(): Promise<void> {
  failure.value = '';

  try {
    await cancel.mutateAsync({
      atPeriodEnd: !cancelNow.value,
      reason: reason.value.trim() === ''
        ? null
        : reason.value.trim(),
    });
  } catch (error) {
    failure.value = `${t('dashboard.settings.billing.plan.cancelFailed')} ${errorText(error, t)}`;
  }

  cancelOpen.value = false;
  cancelNow.value = false;
  reason.value = '';
}
</script>

<template>
  <PanelCard
    data-panel="plan"
    tier="lead"
    pad
    :title="$t('dashboard.settings.billing.plan.title')"
    :description="$t('dashboard.settings.billing.plan.description')"
    :state="state"
    :error-text="$t('dashboard.settings.billing.plan.error')"
    @retry="subscription.refetch()"
  >
    <div v-if="plan" class="grid gap-3">
      <div class="flex flex-wrap items-center gap-3">
        <span data-plan="tier" class="text-lg font-semibold">{{ plan.tierDisplayName }}</span>
        <StatusBadge data-plan="status" :tone="tone" :label="statusLabel" />
      </div>
      <dl class="grid gap-2 text-sm">
        <div v-for="row in dates" :key="row.id" class="flex items-baseline justify-between gap-4">
          <dt class="text-muted-foreground">{{ row.label }}</dt>
          <dd :data-plan="row.id" class="font-medium">{{ row.value }}</dd>
        </div>
        <div class="flex items-baseline justify-between gap-4">
          <dt class="text-muted-foreground">{{ $t('dashboard.settings.billing.plan.customBotName') }}</dt>
          <dd data-plan="custom-bot-name" class="font-medium">{{ yesNo(plan.allowsCustomBotName) }}</dd>
        </div>
        <div class="flex items-baseline justify-between gap-4">
          <dt class="text-muted-foreground">{{ $t('dashboard.settings.billing.plan.prioritySupport') }}</dt>
          <dd data-plan="priority-support" class="font-medium">{{ yesNo(plan.prioritySupport) }}</dd>
        </div>
      </dl>
      <p v-if="plan.isInviteOnlyGrant" data-plan="invite-grant" class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.billing.plan.inviteGrant') }}
      </p>
      <p v-else-if="plan.cancelAtPeriodEnd" data-plan="pending-cancel" class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.billing.plan.pendingCancel') }}
      </p>
      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
    </div>
    <template #footer>
      <div class="flex w-full flex-wrap items-center justify-between gap-2">
        <p data-restricted class="min-w-0 flex-1 basis-64 text-sm text-muted-foreground">
          {{ $t('dashboard.settings.billing.restricted') }}
        </p>
        <div v-if="plan && manageable && can('billing:manage')" class="flex flex-wrap items-center gap-2">
          <Button
            v-if="plan.cancelAtPeriodEnd"
            data-action="resume"
            size="sm"
            :disabled="resume.isPending.value"
            @click="keepPlan"
          >
            {{ $t('dashboard.settings.billing.plan.resume') }}
          </Button>
          <Button
            data-action="payment"
            size="sm"
            variant="outline"
            :disabled="portal.isPending.value"
            @click="openPayment"
          >
            <CreditCard class="size-5" />
            {{ $t('dashboard.settings.billing.plan.payment') }}
          </Button>
          <Button
            v-if="cancellable && !plan.cancelAtPeriodEnd"
            data-action="cancel"
            size="sm"
            variant="ghost"
            @click="cancelOpen = true"
          >
            {{ $t('dashboard.settings.billing.plan.cancel') }}
          </Button>
        </div>
      </div>
    </template>
  </PanelCard>

  <ConfirmDialog
    v-model:open="cancelOpen"
    :title="$t('dashboard.settings.billing.plan.cancelTitle')"
    :description="$t('dashboard.settings.billing.plan.cancelDescription')"
    :consequences="[
      $t('dashboard.settings.billing.plan.cancelLimits'),
      $t('dashboard.settings.billing.plan.cancelFeatures'),
      $t('dashboard.settings.billing.plan.cancelKeep'),
    ]"
    :action="$t('dashboard.settings.billing.plan.cancel')"
    :pending="cancel.isPending.value"
    @confirm="confirmCancel"
  >
    <div class="grid gap-3">
      <LabeledSwitch
        v-if="can('billing:manage')"
        v-model="cancelNow"
        data-field="cancel-now"
        :label="$t('dashboard.settings.billing.plan.cancelNow')"
        :help="$t('dashboard.settings.billing.plan.cancelNowHelp')"
      />
      <Input
        v-model="reason"
        data-field="cancel-reason"
        :aria-label="$t('dashboard.settings.billing.plan.cancelReason')"
        :placeholder="$t('dashboard.settings.billing.plan.cancelReason')"
      />
    </div>
  </ConfirmDialog>
</template>
