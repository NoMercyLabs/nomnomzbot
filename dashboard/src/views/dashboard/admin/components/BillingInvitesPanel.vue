<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Invite codes: mint one (a redemption cap, optionally a tier and the founders badge) or revoke one.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBillingInvites,
  useBillingTiers,
  useCreateInvite,
  useRevokeInvite,
  type InviteCode,
} from '@/api/adminBilling';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';

const { t } = useI18n();

const invites = useBillingInvites();
const tiers = useBillingTiers();
const rows = computed<InviteCode[]>(() => invites.data.value ?? []);
const state = usePanelState(invites, (list) =>
  list.length === 0);

const create = useCreateInvite();
const revoke = useRevokeInvite();

const formOpen = ref(false);
const revoking = ref<InviteCode | null>(null);
const revokeOpen = computed({
  get: () => revoking.value !== null,
  set: (open: boolean) => {
    if (!open) {
      revoking.value = null;
    }
  },
});

const maxRedemptions = ref('1');
const tierId = ref('');
const foundersBadge = ref(false);

async function submit(): Promise<void> {
  await create.mutateAsync({
    maxRedemptions: Number(maxRedemptions.value),
    grantsFoundersBadge: foundersBadge.value,
    grantsTierId: tierId.value === ''
      ? null
      : tierId.value,
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
      <Button variant="outline" size="sm" data-invite-new @click="formOpen = true">
        {{ t("dashboard.admin.billing.invites.new") }}
      </Button>
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="invite in rows" :key="invite.id" data-invite-row class="flex items-center gap-3 px-2 py-2">
        <code class="font-mono">{{ invite.code }}</code>
        <span class="min-w-0 flex-1 text-muted-foreground tabular-nums">
          {{ t('dashboard.admin.billing.invites.redemptions', { used: invite.redemptionCount, max: invite.maxRedemptions }) }}
          <Badge v-if="invite.grantsTierKey" variant="secondary">{{ invite.grantsTierKey }}</Badge>
          <Badge v-if="invite.grantsFoundersBadge" variant="secondary">
            {{ t("dashboard.admin.billing.invites.founder") }}
          </Badge>
        </span>
        <Button variant="outline" size="sm" data-invite-revoke @click="revoking = invite">
          {{ t("dashboard.admin.billing.invites.revoke") }}
        </Button>
      </li>
    </ul>
  </PanelCard>
  <FormDialog
    v-model:open="formOpen"
    data-invite-form
    :title="t('dashboard.admin.billing.invites.new')"
    :action="t('dashboard.admin.billing.invites.create')"
    :pending="create.isPending.value"
    @submit="submit"
  >
    <div class="grid gap-3">
      <Input v-model="maxRedemptions" type="number" :aria-label="t('dashboard.admin.billing.invites.max')" />
      <select v-model="tierId" class="h-9 rounded-md border border-input bg-transparent px-3 text-sm">
        <option value="">{{ t("dashboard.admin.billing.invites.noTier") }}</option>
        <option v-for="tier in tiers.data.value ?? []" :key="tier.id" :value="tier.id">
          {{ tier.displayName }}
        </option>
      </select>
      <label class="flex items-center gap-2 text-sm">
        <input v-model="foundersBadge" type="checkbox" />
        {{ t("dashboard.admin.billing.invites.founder") }}
      </label>
    </div>
  </FormDialog>
  <ConfirmDialog
    v-model:open="revokeOpen"
    :title="t('dashboard.admin.billing.invites.revokeTitle', { code: revoking?.code ?? '' })"
    :action="t('dashboard.admin.billing.invites.revoke')"
    :pending="revoke.isPending.value"
    @confirm="confirmRevoke"
  />
</template>
