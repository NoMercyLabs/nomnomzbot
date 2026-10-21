<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
import RowGroup from '@/components/layout/RowGroup.vue';
import StoredCredentialRow, { type CredentialField } from '@/components/layout/StoredCredentialRow.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
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

const detail = computed<string>(() => {
  const credential = props.credential;
  const parts: string[] = [
    credential.clientId ?? copy('clientIdNone'),
    copy('clientIdLabel', { source: sourceOf(credential.clientIdSource) }),
    copy('secretLabel', { source: sourceOf(credential.secretSource) }),
  ];

  if (credential.apiKeySource) {
    parts.push(copy('apiKeyLabel', { source: sourceOf(credential.apiKeySource) }));
  }

  parts.push(credential.appDecisionRecorded
    ? copy('decisionRecorded')
    : copy('decisionMissing'));

  if (!credential.supported) {
    parts.push(copy('unsupported'));
  }

  return parts.join(' · ');
});

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
    <StoredCredentialRow
      v-if="editable"
      v-model:open="editing"
      partial
      :title="name"
      :detail="detail"
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
    <RowGroup v-else>
      <SummaryRow :title="name" :detail="detail" />
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
