<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// An invite code and the founders badge it can give. Check looks at a code without using it up; Use code
// redeems it and is the one main action of the panel.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import Ticket from '~icons/nnz/ticket';

import {
  useCheckInviteCode,
  useFoundersBadge,
  useRedeemInviteCode,
  type InviteCheck,
  type InviteRedeemed,
} from '@/api/billing';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

const { t, d } = useI18n();
const { channelId } = useCurrentChannel();

const badge = useFoundersBadge(channelId);
const check = useCheckInviteCode(channelId);
const redeem = useRedeemInviteCode(channelId);

const code = ref('');
const checked = ref<InviteCheck | null>(null);
const redeemed = ref<InviteRedeemed | null>(null);
const failure = ref('');

const trimmed = computed(() => code.value.trim());

function dateOf(value: string): string {
  return d(new Date(value), { dateStyle: 'medium' });
}

const badgeLine = computed(() => {
  const found = badge.data.value;

  if (found === null || found === undefined)
    return t('dashboard.settings.billing.badge.none');

  return found.isActive
    ? t('dashboard.settings.billing.badge.active')
    : t('dashboard.settings.billing.badge.inactive');
});

const badgeDetail = computed(() => {
  const found = badge.data.value;

  if (found === null || found === undefined)
    return '';

  const parts = [t('dashboard.settings.billing.badge.since', { date: dateOf(found.grantedAt) })];

  if (found.inviteCode) {
    parts.push(t('dashboard.settings.billing.badge.withCode', { code: found.inviteCode }));
  }

  return parts.join(' · ');
});

const checkLines = computed<string[]>(() => {
  const result = checked.value;

  if (result === null)
    return [];

  if (!result.isValid)
    return [t('dashboard.settings.billing.invite.invalid')];

  const lines = [t('dashboard.settings.billing.invite.valid')];

  if (result.grantsTierKey) {
    lines.push(t('dashboard.settings.billing.invite.grantsTier', { tier: result.grantsTierKey }));
  }

  if (result.grantsFoundersBadge) {
    lines.push(t('dashboard.settings.billing.invite.grantsBadge'));
  }

  lines.push(t('dashboard.settings.billing.invite.remaining', { count: Number(result.remainingRedemptions) }));

  if (result.expiresAt) {
    lines.push(t('dashboard.settings.billing.invite.expires', { date: dateOf(result.expiresAt) }));
  }

  return lines;
});

const redeemedLines = computed<string[]>(() => {
  const result = redeemed.value;

  if (result === null)
    return [];

  const lines = [t('dashboard.settings.billing.invite.redeemed')];

  if (result.grantedTierKey) {
    lines.push(t('dashboard.settings.billing.invite.redeemedTier', { tier: result.grantedTierKey }));
  }

  if (result.grantedFoundersBadge) {
    lines.push(t('dashboard.settings.billing.invite.redeemedBadge'));
  }

  return lines;
});

async function checkCode(): Promise<void> {
  failure.value = '';
  redeemed.value = null;

  try {
    checked.value = await check.mutateAsync(trimmed.value);
  } catch (error) {
    checked.value = null;
    failure.value = `${t('dashboard.settings.billing.invite.failed')} ${errorText(error, t)}`;
  }
}

async function redeemCode(): Promise<void> {
  failure.value = '';
  checked.value = null;

  try {
    redeemed.value = await redeem.mutateAsync(trimmed.value);
    code.value = '';
  } catch (error) {
    failure.value = `${t('dashboard.settings.billing.invite.failed')} ${errorText(error, t)}`;
  }
}
</script>

<template>
  <PanelCard
    data-panel="invite"
    tier="quiet"
    pad
    :title="$t('dashboard.settings.billing.invite.title')"
    :description="$t('dashboard.settings.billing.invite.description')"
  >
    <div class="grid gap-3">
      <div class="flex flex-wrap items-center gap-2">
        <Input
          v-model="code"
          data-field="invite-code"
          class="min-w-0 flex-1 basis-56"
          :aria-label="$t('dashboard.settings.billing.invite.code')"
          :placeholder="$t('dashboard.settings.billing.invite.code')"
          @keydown.enter="checkCode"
        />
        <Button
          data-action="check-code"
          variant="outline"
          :disabled="trimmed === '' || check.isPending.value"
          @click="checkCode"
        >
          {{ $t('dashboard.settings.billing.invite.check') }}
        </Button>
        <Button
          data-action="redeem-code"
          :disabled="trimmed === '' || redeem.isPending.value"
          @click="redeemCode"
        >
          <Ticket class="size-5" />
          {{ $t('dashboard.settings.billing.invite.redeem') }}
        </Button>
      </div>
      <ul v-if="checkLines.length > 0" data-result="check" class="grid gap-1 text-sm">
        <li v-for="line in checkLines" :key="line">{{ line }}</li>
      </ul>
      <ul v-if="redeemedLines.length > 0" data-result="redeemed" class="grid gap-1 text-sm">
        <li v-for="line in redeemedLines" :key="line">{{ line }}</li>
      </ul>
      <p v-if="failure" role="alert" class="text-sm text-destructive">{{ failure }}</p>
      <div class="grid gap-1 border-t border-border pt-3 text-sm">
        <h3 class="font-medium">{{ $t('dashboard.settings.billing.badge.title') }}</h3>
        <p data-badge="state">{{ badgeLine }}</p>
        <p v-if="badgeDetail" data-badge="detail" class="text-muted-foreground">{{ badgeDetail }}</p>
      </div>
    </div>
  </PanelCard>
</template>
