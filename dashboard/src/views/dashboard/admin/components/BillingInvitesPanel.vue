<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
import OptionSelect from '@/components/layout/OptionSelect.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { merge } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

const { t } = useI18n();
const { can } = usePlatformCan();

const invites = useBillingInvites();
const tiers = useBillingTiers();
// "No tier" is the empty choice, so the invite carries no tier unless one is picked.
const tierChoices = computed(() =>
  merge(
    [{ value: '', label: t('dashboard.admin.billing.invites.noTier') }],
    (tiers.data.value ?? []).map((tier) => ({ value: tier.id, label: tier.displayName })),
  ));
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

function grantedTierName(invite: InviteCode): string | undefined {
  return (tiers.data.value ?? []).find((tier) => tier.key === invite.grantsTierKey)?.displayName;
}

// Revoking sets the expiry to now, so a code with a past expiry has ended and cannot be revoked again.
function isEnded(invite: InviteCode): boolean {
  return invite.expiresAt != null && new Date(invite.expiresAt).getTime() <= Date.now();
}

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
      <Button v-if="can('billing:write')"
              variant="outline"
              size="sm"
              data-invite-new
              @click="formOpen = true"
      >
        {{ t("dashboard.admin.billing.invites.new") }}
      </Button>
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li v-for="invite in rows" :key="invite.id" :data-invite-row="invite.id" class="flex items-center gap-3 px-2 py-2">
        <code class="font-mono" :class="{ 'text-muted-foreground line-through': isEnded(invite) }">{{ invite.code }}</code>
        <span class="min-w-0 flex-1 text-muted-foreground tabular-nums">
          {{ t('dashboard.admin.billing.invites.redemptions', { used: invite.redemptionCount, max: invite.maxRedemptions }) }}
          <Badge v-if="isEnded(invite)" variant="outline" data-invite-ended>
            {{ t("dashboard.admin.billing.invites.ended") }}
          </Badge>
          <Badge v-if="grantedTierName(invite)" variant="secondary" data-invite-tier>
            {{ grantedTierName(invite) }}
          </Badge>
          <Badge v-if="invite.grantsFoundersBadge" variant="secondary">
            {{ t("dashboard.admin.billing.invites.founder") }}
          </Badge>
        </span>
        <Button v-if="can('billing:write') && !isEnded(invite)"
                variant="outline"
                size="sm"
                data-invite-revoke
                @click="revoking = invite"
        >
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
      <OptionSelect
        v-model="tierId"
        data-invite-tier
        :options="tierChoices"
        :placeholder="t('dashboard.admin.billing.invites.noTier')"
      />
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
