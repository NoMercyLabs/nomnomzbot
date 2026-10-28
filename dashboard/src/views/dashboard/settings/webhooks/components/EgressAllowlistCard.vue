<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The outside hosts the channel's webhooks may post to. Adding a host is the one visible action; a host can be
// switched off without losing it, and removing one shows what relies on it first.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  fetchAllowedHostBlastRadius,
  useAddAllowedHost,
  useAllowedHosts,
  useRemoveAllowedHost,
  useSwitchAllowedHost,
  type AllowedHost,
} from '@/api/egressAllowlist';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTargetOpen } from '@/composables/useTargetOpen';
import { errorText } from '@/lib/errors';

import { blastLines } from '../../../scripts/components/blastLines';

const { t, te } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();

const hosts = useAllowedHosts(channelId);
const add = useAddAllowedHost(channelId);
const flip = useSwitchAllowedHost(channelId);
const remove = useRemoveAllowedHost(channelId);

const canWrite = computed<boolean>(() => can('egress:allowlist:write'));
const state = usePanelState(hosts, (list: AllowedHost[]) =>
  list.length === 0);

const draft = ref('');
const trimmed = computed<string>(() => draft.value.trim());

const removing = ref<AllowedHost | null>(null);
const consequences = ref<string[]>([]);

const removeOpen = useTargetOpen(removing);

function fail(key: string, reason: string): void {
  toast.error(t(`dashboard.settings.webhooks.allowlist.${key}`, { reason }));
}

function submit(): void {
  if (trimmed.value === '') return;

  add.mutate(trimmed.value, {
    onSuccess: (): void => {
      draft.value = '';
    },
    onError: (error: Error) => fail('addError', error.message),
  });
}

function setEnabled(host: AllowedHost, isEnabled: boolean): void {
  flip.mutate({ host, isEnabled }, { onError: (error: Error) => fail('switchError', error.message) });
}

// The bot counts what relies on the host first; a host whose reach cannot be read is not offered for removal.
async function askRemove(host: AllowedHost): Promise<void> {
  try {
    const lines = blastLines(
      await fetchAllowedHostBlastRadius(channelId.value as string, host),
      t,
      te,
    );

    consequences.value = lines.length > 0
      ? lines
      : [t('dashboard.settings.webhooks.allowlist.nothing')];

    removing.value = host;
  } catch (failure) {
    fail('removeError', errorText(failure, t));
  }
}

function confirmRemove(): void {
  const host = removing.value;

  if (host === null) return;

  remove.mutate(host, {
    onError: (error: Error) => fail('removeError', error.message),
    onSettled: () => {
      removing.value = null;
    },
  });
}
</script>

<template>
  <PanelCard
    data-view="egress-allowlist"
    :title="$t('dashboard.settings.webhooks.allowlist.title')"
    :description="$t('dashboard.settings.webhooks.allowlist.description')"
    :state="state"
    :empty-text="$t('dashboard.settings.webhooks.allowlist.empty')"
    :error-text="$t('dashboard.settings.webhooks.allowlist.loadFailed')"
    @retry="hosts.refetch()"
  >
    <form v-if="canWrite" class="flex gap-2 pb-3" data-add-form @submit.prevent="submit">
      <Input
        v-model="draft"
        data-host-input
        :aria-label="$t('dashboard.settings.webhooks.allowlist.hostLabel')"
        :placeholder="$t('dashboard.settings.webhooks.allowlist.hostPlaceholder')"
      />
      <Button
        type="submit"
        variant="outline"
        data-add
        :disabled="trimmed === '' || add.isPending.value"
      >
        {{ $t("dashboard.settings.webhooks.allowlist.add") }}
      </Button>
    </form>
    <RowGroup>
      <SummaryRow
        v-for="host in hosts.data.value"
        :key="host.id"
        :data-host="host.host"
        :title="host.host"
        :detail="host.isEnabled ? undefined : $t('dashboard.settings.webhooks.allowlist.off')"
      >
        <template v-if="canWrite" #trailing>
          <Switch
            :model-value="host.isEnabled"
            :aria-label="$t('dashboard.settings.webhooks.allowlist.switch', { host: host.host })"
            :data-host-enabled="host.host"
            :disabled="flip.isPending.value"
            @update:model-value="(on: boolean) => setEnabled(host, on)"
          />
          <Button variant="outline" size="sm" :data-host-remove="host.host" @click="askRemove(host)">
            {{ $t("dashboard.settings.webhooks.allowlist.remove") }}
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
    <ConfirmDialog
      v-model:open="removeOpen"
      :title="$t('dashboard.settings.webhooks.allowlist.removeTitle', { host: removing?.host ?? '' })"
      :description="$t('dashboard.settings.webhooks.allowlist.removeDescription')"
      :consequences="consequences"
      :action="$t('dashboard.settings.webhooks.allowlist.removeAction')"
      :pending="remove.isPending.value"
      @confirm="confirmRemove"
    />
  </PanelCard>
</template>
