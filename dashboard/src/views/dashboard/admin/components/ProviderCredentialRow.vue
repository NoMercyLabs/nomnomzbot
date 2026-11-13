<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One provider's OAuth app as a row: name, what is stored and where it comes from. Edit opens the form in a
// dialog. The secret and the API key are write-only: their boxes start empty, only a typed (or changed) field
// is sent, and the bot answers with where each value comes from, never the value.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  useClearProviderCredential,
  useSaveProviderCredential,
  type ProviderCredential,
  type SaveProviderCredentialRequest,
} from '@/api/adminProviders';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import ConnectionRow from '@/components/layout/ConnectionRow.vue';
import ProviderMark from '@/components/layout/ProviderMark.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StoredCredentialRow, { type CredentialField } from '@/components/layout/StoredCredentialRow.vue';
import { usePlatformCan } from '@/composables/usePlatformCan';

const props = defineProps<{ credential: ProviderCredential }>();

const { t } = useI18n();
const { can } = usePlatformCan();
const copy = (key: string, values: Record<string, string | number> = {}): string =>
  t(`dashboard.admin.providers.${key}`, values);

const save = useSaveProviderCredential();
const clear = useClearProviderCredential();

const editing = ref(false);
const confirmingClear = ref(false);
const error = ref<string | undefined>(undefined);

const sourceOf = (source: string): string => copy(`source.${source}`);
const name = computed<string>(() => {
  const provider = props.credential.provider;

  return provider.charAt(0).toUpperCase() + provider.slice(1);
});
const editable = computed<boolean>(() => props.credential.supported && can('iam:manage'));

// Operator-only: a platform app the admin has not set is disconnected, never the streamer's attention tone.
const configured = computed<boolean>(() =>
  props.credential.clientIdSource !== 'unset' && props.credential.secretSource !== 'unset');
const clientIdText = computed<string>(() => props.credential.clientId ?? copy('clientIdNone'));

const sources = computed<string>(() => {
  const credential = props.credential;
  const parts: string[] = [
    copy('clientIdLabel', { source: sourceOf(credential.clientIdSource) }),
    copy('secretLabel', { source: sourceOf(credential.secretSource) }),
  ];

  if (credential.apiKeySource) {
    parts.push(copy('apiKeyLabel', { source: sourceOf(credential.apiKeySource) }));
  }

  if (credential.botTokenSource) {
    parts.push(copy('botTokenLabel', { source: sourceOf(credential.botTokenSource) }));
  }

  parts.push(credential.appDecisionRecorded
    ? copy('decisionRecorded')
    : copy('decisionMissing'));

  if (!credential.supported) {
    parts.push(copy('unsupported'));
  }

  return parts.join(' · ');
});
const detail = computed<string>(() => `${clientIdText.value} · ${sources.value}`);

const fields = computed<CredentialField[]>(() => {
  const list: CredentialField[] = [
    {
      name: 'clientId',
      label: copy('fields.clientId'),
      value: props.credential.clientId ?? '',
    },
    {
      name: 'clientSecret',
      label: copy('fields.clientSecret'),
      secret: true,
    },
  ];

  if (props.credential.provider === 'youtube') {
    list.push({
      name: 'apiKey',
      label: copy('fields.apiKey'),
      secret: true,
    });
  }

  if (props.credential.provider === 'discord') {
    list.push({
      name: 'botToken',
      label: copy('fields.botToken'),
      secret: true,
    });
  }

  return list;
});

function bodyOf(values: Record<string, string>): SaveProviderCredentialRequest {
  const request: SaveProviderCredentialRequest = {};
  const clientId = (values.clientId ?? '').trim();

  if (clientId !== '' && clientId !== (props.credential.clientId ?? '')) {
    request.clientId = clientId;
  }

  if ((values.clientSecret ?? '') !== '') {
    request.clientSecret = values.clientSecret;
  }

  if ((values.apiKey ?? '') !== '') {
    request.apiKey = values.apiKey;
  }

  if ((values.botToken ?? '') !== '') {
    request.botToken = values.botToken;
  }

  return request;
}

function submit(values: Record<string, string>): void {
  error.value = undefined;

  save.mutate(
    { provider: props.credential.provider, body: bodyOf(values) },
    {
      onSuccess: () => {
        toast.success(copy('saved'));
        editing.value = false;
      },
      onError: (failure: Error) => {
        error.value = copy('saveError', { reason: failure.message });
      },
    },
  );
}

function wipe(): void {
  clear.mutate(props.credential.provider, {
    onSuccess: () => {
      toast.success(copy('cleared'));
      confirmingClear.value = false;
      editing.value = false;
    },
    onError: (failure: Error) => {
      confirmingClear.value = false;
      error.value = copy('clearError', { reason: failure.message });
    },
  });
}
</script>

<template>
  <div :data-provider="credential.provider">
    <RowGroup>
      <ConnectionRow
        :name="name"
        :state="configured ? 'connected' : 'disconnected'"
        :detail="editable ? undefined : detail"
      >
        <template #leading>
          <ProviderMark :provider="credential.provider" />
        </template>
        <template v-if="editable" #credential>
          <StoredCredentialRow
            v-model:open="editing"
            inline
            partial
            :title="clientIdText"
            :detail="sources"
            :edit-label="copy('edit')"
            :dialog-title="copy('dialogTitle', { provider: name })"
            :dialog-description="copy('secretHint')"
            :save-label="copy('save')"
            :remove-label="copy('clear')"
            remove-action="clear"
            :fields="fields"
            removable
            :pending="save.isPending.value"
            :removing="clear.isPending.value"
            :error="error"
            @submit="submit"
            @remove="confirmingClear = true"
          />
        </template>
      </ConnectionRow>
    </RowGroup>
    <ConfirmDialog
      v-model:open="confirmingClear"
      :title="copy('clearTitle', { provider: name })"
      :description="copy('clearConfirmText')"
      :action="copy('clearConfirm')"
      :pending="clear.isPending.value"
      @confirm="wipe"
    />
  </div>
</template>
