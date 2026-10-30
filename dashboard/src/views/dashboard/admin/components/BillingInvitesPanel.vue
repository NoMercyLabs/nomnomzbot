<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Invite codes: mint one that gives any tier (a hidden one too) for N months or forever, the founders badge,
// or both, with a redemption cap and an optional expiry; copy its share link; revoke it. The form names what
// the invite gives before it is made, and each row shows what it gives and how many uses are left.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBillingInvites,
  useCreateInvite,
  useRevokeInvite,
  type InviteCode,
} from '@/api/adminBilling';
import { tierNameOf } from '@/api/billing';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { merge } from '@/lib/array';
import { inviteLink } from '@/lib/botAddress';
import { numberOrNull } from '@/lib/number';
import { fromLocalInput } from '@/lib/time';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import { useTime } from '@/composables/useTime';

import TierLengthFields from './TierLengthFields.vue';
import { useTierLength } from './tierLength';

const { t } = useI18n();
const { can } = usePlatformCan();
const time = useTime();

const invites = useBillingInvites();
const {
  tiers,
  tierId,
  months,
  forever,
  tierChoices,
  monthCount,
  lengthValid,
  tierLabel,
  reset: resetLength,
} = useTierLength();
// "No tier" is the empty choice: a badge-only invite carries no tier and no length.
const choices = computed(() =>
  merge([{ value: '', label: t('dashboard.admin.billing.invites.noTier') }], tierChoices.value));
const rows = computed<InviteCode[]>(() => invites.data.value ?? []);
const state = usePanelState(invites, (list) =>
  list.length === 0);

const create = useCreateInvite();
const revoke = useRevokeInvite();

const formOpen = ref(false);
const maxRedemptions = ref('1');
const expires = ref('');
const foundersBadge = ref(false);
const created = ref<InviteCode | null>(null);
const createdSummary = ref('');
const revoking = ref<InviteCode | null>(null);

const createdOpen = computed({
  get: () => created.value !== null,
  set: (open: boolean) => {
    if (!open) {
      created.value = null;
    }
  },
});
const revokeOpen = computed({
  get: () => revoking.value !== null,
  set: (open: boolean) => {
    if (!open) {
      revoking.value = null;
    }
  },
});
const createdLink = computed<string>(() =>
  created.value === null
    ? ''
    : inviteLink(created.value.code));

const maxCount = computed<number>(() => Number(maxRedemptions.value));
const hasTier = computed<boolean>(() => tierId.value !== '');
// An invite gives a tier for a valid length, the founders badge, or both; one that gives nothing is held back.
const gives = computed<boolean>(() =>
  hasTier.value
    ? lengthValid.value
    : foundersBadge.value);
const formReady = computed<boolean>(() =>
  Number.isInteger(maxCount.value) && maxCount.value >= 1 && gives.value);

const summary = computed<string>(() => {
  if (!gives.value)
    return '';

  if (!hasTier.value)
    return t('dashboard.admin.billing.invites.givesBadge');

  const tier = tierLabel(tierId.value);

  if (forever.value)
    return t('dashboard.admin.billing.invites.givesForever', { tier });

  const count = monthCount.value;

  return t('dashboard.admin.billing.invites.givesMonths', { tier, count }, count);
});

function grantedTier(invite: InviteCode): string {
  return tierNameOf(tiers.data.value, invite.grantsTierKey) ?? '';
}

// A tier invite runs for its months, or forever when it names none.
function lengthText(invite: InviteCode): string {
  const count = numberOrNull(invite.grantsMonths);

  if (count === null)
    return t('dashboard.admin.billing.invites.forever');

  return t('dashboard.admin.billing.invites.months', { count }, count);
}

function usesLeft(invite: InviteCode): string {
  const count = Math.max(0, Number(invite.maxRedemptions) - Number(invite.redemptionCount));

  return t('dashboard.admin.billing.invites.left', { count }, count);
}

// Revoking sets the expiry to now, so a code with a past expiry has ended and cannot be revoked again.
function isEnded(invite: InviteCode): boolean {
  return invite.expiresAt != null && new Date(invite.expiresAt).getTime() <= Date.now();
}

function openForm(): void {
  resetLength();
  maxRedemptions.value = '1';
  expires.value = '';
  foundersBadge.value = false;
  formOpen.value = true;
}

async function submit(): Promise<void> {
  const ends = hasTier.value && !forever.value;

  createdSummary.value = summary.value;

  created.value = await create.mutateAsync({
    maxRedemptions: maxCount.value,
    grantsFoundersBadge: foundersBadge.value,
    grantsTierId: hasTier.value
      ? tierId.value
      : null,
    grantsMonths: ends
      ? monthCount.value
      : null,
    expiresAt: expires.value === ''
      ? null
      : fromLocalInput(expires.value),
  });

  formOpen.value = false;
}

async function confirmRevoke(): Promise<void> {
  if (revoking.value === null) return;

  await revoke.mutateAsync(revoking.value.id);
  revoking.value = null;
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.billing.invites.title')"
    :state="state"
    :empty-text="t('dashboard.admin.billing.invites.empty')"
    :error-text="t('dashboard.admin.billing.invites.error')"
    @retry="invites.refetch()"
  >
    <template #actions>
      <Button v-if="can('billing:write')"
              variant="outline"
              size="sm"
              data-invite-new
              @click="openForm"
      >
        {{ t("dashboard.admin.billing.invites.new") }}
      </Button>
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="invite in rows"
          :key="invite.id"
          :data-invite-row="invite.id"
          class="flex flex-wrap items-center gap-3 px-2 py-2"
      >
        <code class="font-mono" :class="{ 'text-muted-foreground line-through': isEnded(invite) }">{{ invite.code }}</code>
        <span class="flex min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-1 text-muted-foreground">
          <template v-if="grantedTier(invite)">
            <Badge variant="secondary" data-invite-tier>{{ grantedTier(invite) }}</Badge>
            <span data-invite-length>{{ lengthText(invite) }}</span>
          </template>
          <Badge v-if="invite.grantsFoundersBadge" variant="secondary">
            {{ t("dashboard.admin.billing.invites.founder") }}
          </Badge>
          <span class="tabular-nums">
            {{ t('dashboard.admin.billing.invites.redemptions', { used: invite.redemptionCount, max: invite.maxRedemptions }) }}
          </span>
          <Badge v-if="isEnded(invite)" variant="outline" data-invite-ended>
            {{ t("dashboard.admin.billing.invites.ended") }}
          </Badge>
          <template v-else>
            <span data-invite-left class="tabular-nums">{{ usesLeft(invite) }}</span>
            <span v-if="invite.expiresAt" data-invite-expires-at>
              {{ t('dashboard.admin.billing.invites.expires', { date: time.date(invite.expiresAt) }) }}
            </span>
          </template>
        </span>
        <template v-if="!isEnded(invite)">
          <CopyLinkButton :url="inviteLink(invite.code)" :label="t('dashboard.admin.billing.invites.copy')" />
          <Button v-if="can('billing:write')"
                  variant="ghost"
                  data-invite-revoke
                  @click="revoking = invite"
          >
            {{ t("dashboard.admin.billing.invites.revoke") }}
          </Button>
        </template>
      </li>
    </ul>
  </PanelCard>
  <FormDialog
    v-model:open="formOpen"
    data-invite-form
    :title="t('dashboard.admin.billing.invites.new')"
    :action="t('dashboard.admin.billing.invites.create')"
    :pending="create.isPending.value"
    :disabled="!formReady"
    @submit="submit"
  >
    <TierLengthFields
      id="invite"
      v-model:tier-id="tierId"
      v-model:months="months"
      v-model:forever="forever"
      :choices="choices"
    />
    <div class="grid gap-2">
      <Label for="invite-max">{{ t('dashboard.admin.billing.invites.max') }}</Label>
      <Input id="invite-max"
             v-model="maxRedemptions"
             data-invite-max
             type="number"
             class="w-28"
             min="1"
      />
    </div>
    <div class="grid gap-2">
      <Label for="invite-expires">{{ t('dashboard.admin.billing.invites.expiresLabel') }}</Label>
      <Input id="invite-expires"
             v-model="expires"
             data-invite-expires
             type="datetime-local"
             class="w-fit"
      />
    </div>
    <LabeledSwitch v-model="foundersBadge" data-invite-founder :label="t('dashboard.admin.billing.invites.founder')" />
    <p v-if="summary" data-invite-summary class="text-sm text-muted-foreground">
      {{ summary }}
    </p>
  </FormDialog>
  <Dialog v-model:open="createdOpen">
    <DialogContent data-invite-created class="sm:max-w-130" :show-close-button="false">
      <DialogTitle>{{ t('dashboard.admin.billing.invites.createdTitle', { code: created?.code ?? '' }) }}</DialogTitle>
      <DialogDescription>{{ t('dashboard.admin.billing.invites.createdDescription') }}</DialogDescription>
      <p v-if="createdSummary" class="text-sm">{{ createdSummary }}</p>
      <code data-invite-created-link class="block rounded-sm bg-muted p-3 font-mono text-sm break-all select-all">
        {{ createdLink }}
      </code>
      <DialogFooter>
        <CopyLinkButton :url="createdLink" :label="t('dashboard.admin.billing.invites.copy')" />
        <Button data-invite-created-done @click="created = null">
          {{ t('dashboard.admin.billing.invites.done') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
  <ConfirmDialog
    v-model:open="revokeOpen"
    :title="t('dashboard.admin.billing.invites.revokeTitle', { code: revoking?.code ?? '' })"
    :consequences="[t('dashboard.admin.billing.invites.revokeConsequence')]"
    :action="t('dashboard.admin.billing.invites.revoke')"
    :pending="revoke.isPending.value"
    @confirm="confirmRevoke"
  />
</template>
