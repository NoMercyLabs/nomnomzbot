<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The setup details of one supporter source. A provider delivers its own way, so the dialog shows the
// delivery address (webhook sources) and never asks for a mode. A secret goes to the bot and is never shown
// again: the box is emptied after a save and only says whether one is stored. OAuth providers take a
// connected account instead of a secret. Save is the one primary action and stays off until something changed.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useConnections } from '@/api/home';
import {
  supporterSources,
  useSaveSupporterConnection,
  useSupporterConnections,
  type SupporterSourceKey,
} from '@/api/supporters';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { FieldInfo } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

// The select cannot hold an empty value, so "no account" is this key and goes out as no grant.
const NO_GRANT = '__none';

const sourceKey = defineModel<SupporterSourceKey | null>('source', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const connections = useSupporterConnections(channelId);
const integrations = useConnections(channelId);
const save = useSaveSupporterConnection(channelId);

const secret = ref('');
const grantId = ref(NO_GRANT);
const enabled = ref(true);

const definition = computed(() =>
  sourceKey.value === null
    ? null
    : supporterSources[sourceKey.value],
);

const name = computed<string>(() =>
  sourceKey.value === null
    ? ''
    : t(`dashboard.supporters.connections.sources.${sourceKey.value}`),
);

const stored = computed(() =>
  connections.data.value?.find((entry) => entry.sourceKey === sourceKey.value),
);

const grants = computed(() =>
  (integrations.data.value ?? []).filter(
    (entry) => entry.provider === sourceKey.value && entry.connected && entry.connectionId,
  ),
);

const title = computed<string>(() =>
  t(
    stored.value === undefined
      ? 'dashboard.supporters.connections.titleNew'
      : 'dashboard.supporters.connections.titleStored',
    { name: name.value },
  ),
);

const secretPlaceholder = computed<string>(() =>
  stored.value?.hasSecret
    ? t('dashboard.supporters.connections.secretStored')
    : t('dashboard.supporters.connections.secretNone'),
);

const changed = computed<boolean>(
  () =>
    secret.value.trim() !== '' ||
    (definition.value?.oauth === true && grantId.value !== NO_GRANT) ||
    enabled.value !== (stored.value?.isEnabled ?? true),
);

// Opening a source fills the switch with what is stored; a new one starts at the default (on).
watch(
  sourceKey,
  () => {
    secret.value = '';
    grantId.value = NO_GRANT;
    enabled.value = stored.value?.isEnabled ?? true;
    save.reset();
  },
  { immediate: true },
);

function close(open: boolean): void {
  if (!open) {
    sourceKey.value = null;
  }
}

function submit(): void {
  const current = definition.value;

  if (current === null || sourceKey.value === null) return;

  const typed = secret.value.trim();

  save.mutate(
    {
      sourceKey: sourceKey.value,
      connectionMode: current.mode,
      authSecret: current.oauth || typed === ''
        ? null
        : typed,
      integrationConnectionId: current.oauth && grantId.value !== NO_GRANT
        ? grantId.value
        : null,
      isEnabled: enabled.value,
    },
    {
      onSuccess: () => {
        secret.value = '';
        sourceKey.value = null;
        toast.success(t('dashboard.supporters.connections.saved'));
      },
    },
  );
}
</script>

<template>
  <FormDialog
    :open="sourceKey !== null"
    :title="title"
    :description="definition === null ? undefined : t(`dashboard.supporters.connections.mode.${definition.mode}`)"
    :action="t('dashboard.supporters.connections.save')"
    :pending="save.isPending.value"
    :disabled="!changed"
    :error="save.isError.value ? t('dashboard.supporters.connections.saveFailed') : undefined"
    data-view="supporter-source-dialog"
    @update:open="close"
    @submit="submit"
  >
    <template #header-action>
      <LabeledSwitch v-model="enabled" :label="t('dashboard.supporters.connections.enabled')" />
    </template>
    <div v-if="stored?.endpointUrl" class="grid gap-1.5">
      <Label for="supporter-endpoint">
        {{ t("dashboard.supporters.connections.endpoint") }}
        <FieldInfo>{{ t("dashboard.supporters.connections.endpointHelp") }}</FieldInfo>
      </Label>
      <Input id="supporter-endpoint" data-field="endpointUrl" :model-value="stored.endpointUrl" readonly />
    </div>
    <div v-if="definition?.oauth" class="grid gap-1.5">
      <Label for="supporter-grant">{{ t("dashboard.supporters.connections.grant") }}</Label>
      <Select v-model="grantId">
        <SelectTrigger id="supporter-grant" data-field="integrationConnectionId" class="w-full">
          <SelectValue />
        </SelectTrigger>
        <SelectContent position="popper">
          <SelectGroup>
            <SelectItem :value="NO_GRANT">
              {{ t("dashboard.supporters.connections.grantNone") }}
            </SelectItem>
            <SelectItem
              v-for="grant in grants"
              :key="grant.connectionId ?? ''"
              :value="grant.connectionId ?? ''"
            >
              {{ grant.accountName ?? grant.provider }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <p v-if="grants.length === 0" class="text-sm text-muted-foreground">
        {{ t("dashboard.supporters.connections.noAccount", { name }) }}
      </p>
    </div>
    <div v-else class="grid gap-1.5">
      <Label for="supporter-secret">
        {{ t("dashboard.supporters.connections.secret") }}
        <FieldInfo>{{ t("dashboard.supporters.connections.secretHelp") }}</FieldInfo>
      </Label>
      <Input
        id="supporter-secret"
        v-model="secret"
        data-field="authSecret"
        type="password"
        autocomplete="off"
        :placeholder="secretPlaceholder"
      />
    </div>
  </FormDialog>
</template>
