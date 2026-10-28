<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The sign-ins that lead to the person's one account. The main sign-in cannot be unlinked; any other asks first.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useMyIdentities, useUnlinkIdentity, type LinkedIdentity } from '@/api/viewerAccount';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import PlatformLogo from '@/components/layout/PlatformLogo.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

const { t, te } = useI18n();
const time = useTime();

const identities = useMyIdentities();
const unlink = useUnlinkIdentity();
const state = usePanelState(identities, (rows) =>
  rows.length === 0);
const target = ref<LinkedIdentity | null>(null);

function providerName(provider: string): string {
  const key = `viewer.account.accounts.provider.${provider}`;

  return te(key)
    ? t(key)
    : provider;
}

function nameOf(row: LinkedIdentity): string {
  return row.providerDisplayName ?? row.providerUsername;
}

function confirmUnlink(): void {
  const id = target.value?.id;

  if (!id) return;

  unlink.mutate(id, {
    onSuccess: () => {
      target.value = null;
    },
  });
}
</script>

<template>
  <PanelCard
    :title="$t('viewer.account.accounts.title')"
    :description="$t('viewer.account.accounts.description')"
    :state="state"
    :empty-text="$t('viewer.account.accounts.empty')"
    :error-text="$t('viewer.account.accounts.error')"
    data-viewer-accounts
    @retry="identities.refetch()"
  >
    <RowGroup>
      <SummaryRow
        v-for="row in identities.data.value ?? []"
        :key="row.providerUserId"
        :title="nameOf(row)"
        :detail="providerName(row.provider)"
        :meta="$t('viewer.account.accounts.linked', { date: time.date(row.linkedAt) })"
        data-viewer-account-row
      >
        <template #leading>
          <PlatformLogo :platform="row.provider" class="size-5 shrink-0" />
        </template>
        <template #end>
          <Badge v-if="row.isPrimary" variant="secondary" class="mr-3">
            {{ $t('viewer.account.accounts.primary') }}
          </Badge>
          <!-- The unlink call needs the row id; a row the bot sends without one offers no button it cannot honor. -->
          <Button v-else-if="row.id"
                  variant="outline"
                  size="sm"
                  data-unlink
                  @click="target = row"
          >
            {{ $t('viewer.account.accounts.unlink') }}
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>

  <ConfirmDialog
    :open="target !== null"
    :title="$t('viewer.account.accounts.unlinkTitle', { name: target ? nameOf(target) : '' })"
    :description="$t('viewer.account.accounts.unlinkDescription')"
    :consequences="[$t('viewer.account.accounts.unlinkConsequence')]"
    :action="$t('viewer.account.accounts.unlink')"
    :pending="unlink.isPending.value"
    @update:open="(open: boolean) => { if (!open) target = null; }"
    @confirm="confirmUnlink"
  />
</template>
