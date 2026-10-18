<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Where a channel's tips and memberships come from. A provider is picked once and delivers its own way,
// so the form shows the mode instead of asking for it. A secret goes to the bot and is never shown again:
// the box is emptied after a save and only says whether one is stored. OAuth providers take a connected
// grant instead of a secret.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useConnections } from '@/api/home';
import {
  supporterSources,
  useSaveSupporterConnection,
  useSupporterConnections,
  type SupporterSourceKey,
} from '@/api/supporters';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const selectClass =
  'h-9 rounded-md border border-input bg-transparent px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const connections = useSupporterConnections(channelId);
const integrations = useConnections(channelId);
const save = useSaveSupporterConnection(channelId);

const sourceKeys = Object.keys(supporterSources) as SupporterSourceKey[];
const sourceKey = ref<SupporterSourceKey>(sourceKeys[0] as SupporterSourceKey);
const secret = ref('');
const grantId = ref('');
const enabled = ref(true);

const source = computed(() => supporterSources[sourceKey.value]);
const storedConnection = computed(() =>
  connections.data.value?.find((entry) => entry.sourceKey === sourceKey.value));
const grants = computed(() =>
  (integrations.data.value ?? []).filter(
    (entry) => entry.provider === sourceKey.value && entry.connected && entry.connectionId,
  ));
const secretPlaceholder = computed(() =>
  storedConnection.value?.hasSecret
    ? t('dashboard.supporters.connections.secretStored')
    : t('dashboard.supporters.connections.secretNone'));

function submit(): void {
  const oauth = source.value.oauth;
  const typed = secret.value.trim();

  save.mutate(
    {
      sourceKey: sourceKey.value,
      connectionMode: source.value.mode,
      authSecret: oauth || typed === ''
        ? null
        : typed,
      integrationConnectionId: oauth && grantId.value !== ''
        ? grantId.value
        : null,
      isEnabled: enabled.value,
    },
    {
      onSuccess: () => {
        secret.value = '';
        toast.success(t('dashboard.supporters.connections.saved'));
      },
      onError: () => toast.error(t('dashboard.supporters.connections.saveFailed')),
    },
  );
}
</script>

<template>
  <PanelCard
    data-view="supporter-connections"
    pad
    :title="t('dashboard.supporters.connections.title')"
    :description="t('dashboard.supporters.connections.description')"
  >
    <form class="flex flex-wrap items-end gap-3" @submit.prevent="submit">
      <label class="flex min-w-40 flex-col gap-1 text-sm font-medium">
        {{ t('dashboard.supporters.connections.source') }}
        <select v-model="sourceKey" data-field="sourceKey" :class="selectClass">
          <option v-for="key in sourceKeys" :key="key" :value="key">
            {{ t(`dashboard.supporters.connections.sources.${key}`) }}
          </option>
        </select>
        <span class="text-sm font-normal text-muted-foreground">
          {{ t(`dashboard.supporters.connections.mode.${source.mode}`) }}
        </span>
      </label>
      <label v-if="source.oauth" class="flex min-w-48 flex-1 flex-col gap-1 text-sm font-medium">
        {{ t('dashboard.supporters.connections.grant') }}
        <select v-model="grantId" data-field="integrationConnectionId" :class="selectClass">
          <option value="">
            {{ t('dashboard.supporters.connections.grantNone') }}
          </option>
          <option v-for="grant in grants" :key="grant.connectionId ?? ''" :value="grant.connectionId">
            {{ grant.accountName ?? grant.provider }}
          </option>
        </select>
      </label>
      <label v-else class="flex min-w-48 flex-1 flex-col gap-1 text-sm font-medium">
        {{ t('dashboard.supporters.connections.secret') }}
        <Input
          v-model="secret"
          data-field="authSecret"
          type="password"
          autocomplete="off"
          :placeholder="secretPlaceholder"
        />
      </label>
      <label class="flex items-center gap-2 pb-2 text-sm font-medium">
        <Switch
          v-model="enabled"
          :aria-label="t('dashboard.supporters.connections.enabled')"
        />
        {{ t('dashboard.supporters.connections.enabled') }}
      </label>
      <Button type="submit" :disabled="save.isPending.value">
        {{ t('dashboard.supporters.connections.save') }}
      </Button>
    </form>
  </PanelCard>
</template>
