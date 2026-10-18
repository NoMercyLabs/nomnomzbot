<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// API and devices: the keys that let outside tools (Stream Deck, Touch Portal, scripts) use this channel,
// and the pairing of those devices. A key's secret shows once, on create and on rotate.
import Add from '~icons/nnz/add';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useAutomationTokens,
  useCreateAutomationToken,
  useRevokeAutomationToken,
  useRotateAutomationToken,
  type AutomationToken,
  type IssuedToken,
  type NewAutomationToken,
} from '@/api/automationTokens';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import CreateTokenDialog from './components/CreateTokenDialog.vue';
import EventsPanel from './components/EventsPanel.vue';
import PairingPanel from './components/PairingPanel.vue';
import SecretDialog from './components/SecretDialog.vue';
import TokensTable from './components/TokensTable.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const tokens = useAutomationTokens(channelId);
const create = useCreateAutomationToken(channelId);
const rotate = useRotateAutomationToken(channelId);
const revoke = useRevokeAutomationToken(channelId);

const creating = ref(false);
const createError = ref<string | undefined>(undefined);
const issued = ref<IssuedToken | undefined>(undefined);
const secretShown = ref(false);
const rotating = ref<AutomationToken | undefined>(undefined);
const revoking = ref<AutomationToken | undefined>(undefined);
const rotateOpen = ref(false);
const revokeOpen = ref(false);
const actionError = ref<string | undefined>(undefined);

const state = computed<PanelState>(() => {
  if (tokens.isError.value)
    return 'error';

  if (tokens.isPending.value)
    return 'loading';

  return (tokens.data.value ?? []).length === 0
    ? 'empty'
    : 'ready';
});

function showSecret(result: IssuedToken): void {
  issued.value = result;
  secretShown.value = true;
}

async function submitCreate(request: NewAutomationToken): Promise<void> {
  createError.value = undefined;

  try {
    showSecret(await create.mutateAsync(request));
    creating.value = false;
  } catch (failure) {
    createError.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');
  }
}

function askRotate(token: AutomationToken): void {
  rotating.value = token;
  rotateOpen.value = true;
}

function askRevoke(token: AutomationToken): void {
  revoking.value = token;
  revokeOpen.value = true;
}

async function confirmRotate(): Promise<void> {
  if (rotating.value === undefined) return;

  actionError.value = undefined;

  try {
    showSecret(await rotate.mutateAsync(rotating.value.id));
    rotateOpen.value = false;
  } catch (failure) {
    actionError.value = errorText(failure, t);
    rotateOpen.value = false;
  }
}

async function confirmRevoke(): Promise<void> {
  if (revoking.value === undefined) return;

  actionError.value = undefined;

  try {
    await revoke.mutateAsync(revoking.value.id);
  } catch (failure) {
    actionError.value = errorText(failure, t);
  }

  revokeOpen.value = false;
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.apiDevices')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <div data-view="api-devices" class="grid gap-4">
      <PanelCard
        data-panel="tokens"
        tier="lead"
        :title="$t('dashboard.settings.apiDevices.tokens.title')"
        :description="$t('dashboard.settings.apiDevices.tokens.description')"
        :state="state"
        :empty-text="$t('dashboard.settings.apiDevices.tokens.empty')"
        :error-text="$t('dashboard.settings.apiDevices.tokens.error')"
        @retry="tokens.refetch()"
      >
        <template v-if="can('automation:tokens:write')" #actions>
          <Button data-new-token @click="creating = true">
            <Add class="size-5" />
            {{ $t('dashboard.settings.apiDevices.tokens.new') }}
          </Button>
        </template>
        <TokensTable :tokens="tokens.data.value ?? []" @rotate="askRotate" @revoke="askRevoke" />
      </PanelCard>
      <p v-if="actionError" role="alert" class="text-sm text-destructive">{{ actionError }}</p>

      <PairingPanel v-if="can('automation:tokens:write')" />
      <EventsPanel />
    </div>
  </PageBody>

  <CreateTokenDialog
    v-model:open="creating"
    :pending="create.isPending.value"
    :error="createError"
    @submit="submitCreate"
  />
  <SecretDialog
    v-if="issued"
    v-model:open="secretShown"
    :secret="issued.secret"
    :name="issued.token.name"
  />
  <ConfirmDialog
    v-model:open="rotateOpen"
    :title="$t('dashboard.settings.apiDevices.rotate.title', { name: rotating?.name ?? '' })"
    :description="$t('dashboard.settings.apiDevices.rotate.description')"
    :consequences="[$t('dashboard.settings.apiDevices.rotate.consequence')]"
    :action="$t('dashboard.settings.apiDevices.rotate.action')"
    :pending="rotate.isPending.value"
    @confirm="confirmRotate"
  />
  <ConfirmDialog
    v-model:open="revokeOpen"
    :title="$t('dashboard.settings.apiDevices.revoke.title', { name: revoking?.name ?? '' })"
    :description="$t('dashboard.settings.apiDevices.revoke.description')"
    :consequences="[$t('dashboard.settings.apiDevices.revoke.consequence')]"
    :action="$t('dashboard.settings.apiDevices.revoke.action')"
    :pending="revoke.isPending.value"
    @confirm="confirmRevoke"
  />
</template>
