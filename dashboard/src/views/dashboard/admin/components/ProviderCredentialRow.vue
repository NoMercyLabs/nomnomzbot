<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One provider's OAuth app. The secret and the API key are write-only: the boxes start empty, only a typed
// field is sent, and the bot answers with where each value comes from, never the value.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePlatformCan } from '@/composables/usePlatformCan';
import { toast } from 'vue-sonner';

import {
  useClearProviderCredential,
  useSaveProviderCredential,
  type ProviderCredential,
  type SaveProviderCredentialRequest,
} from '@/api/adminProviders';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

const props = defineProps<{ credential: ProviderCredential }>();

const { t } = useI18n();
const { can } = usePlatformCan();
const copy = (key: string, values: Record<string, string | number> = {}): string =>
  t(`dashboard.admin.providers.${key}`, values);

const save = useSaveProviderCredential();
const clear = useClearProviderCredential();

const clientId = ref('');
const clientSecret = ref('');
const apiKey = ref('');
const confirmingClear = ref(false);

const isYouTube = computed<boolean>(() => props.credential.provider === 'youtube');
const sourceOf = (source: string): string => copy(`source.${source}`);

function body(): SaveProviderCredentialRequest {
  const request: SaveProviderCredentialRequest = {};

  if (clientId.value.trim() !== '') {
    request.clientId = clientId.value.trim();
  }

  if (clientSecret.value !== '') {
    request.clientSecret = clientSecret.value;
  }

  if (apiKey.value !== '') {
    request.apiKey = apiKey.value;
  }

  return request;
}

const canSave = computed<boolean>(
  () => Object.keys(body()).length > 0 && !save.isPending.value && props.credential.supported,
);

function submit(): void {
  save.mutate(
    { provider: props.credential.provider, body: body() },
    {
      onSuccess: () => {
        toast.success(copy('saved'));
        clientId.value = '';
        clientSecret.value = '';
        apiKey.value = '';
      },
      onError: (error: Error) => toast.error(copy('saveError', { reason: error.message })),
    },
  );
}

function wipe(): void {
  clear.mutate(props.credential.provider, {
    onSuccess: () => {
      toast.success(copy('cleared'));
      confirmingClear.value = false;
    },
    onError: (error: Error) => toast.error(copy('clearError', { reason: error.message })),
  });
}
</script>

<template>
  <li :data-provider="credential.provider" class="flex flex-col gap-3 px-4 py-3">
    <div class="flex flex-wrap items-center gap-2">
      <span class="text-sm font-medium capitalize">{{ credential.provider }}</span>
      <Badge variant="secondary">{{ credential.appDecisionRecorded ? copy('decisionRecorded') : copy('decisionMissing') }}</Badge>
      <Badge data-secret-source variant="outline">{{ copy('secretLabel', { source: sourceOf(credential.secretSource) }) }}</Badge>
      <Badge variant="outline">{{ copy('clientIdLabel', { source: sourceOf(credential.clientIdSource) }) }}</Badge>
      <Badge v-if="credential.apiKeySource" variant="outline">
        {{ copy('apiKeyLabel', { source: sourceOf(credential.apiKeySource) }) }}
      </Badge>
    </div>
    <p class="text-sm text-muted-foreground tabular-nums">{{ credential.clientId ?? copy('clientIdNone') }}</p>
    <p v-if="!credential.supported" class="text-sm text-muted-foreground">{{ copy('unsupported') }}</p>
    <div v-else class="flex flex-col gap-3">
      <div class="flex flex-col gap-1.5">
        <Label :for="`${credential.provider}-client-id`">{{ copy('fields.clientId') }}</Label>
        <Input :id="`${credential.provider}-client-id`" v-model="clientId" data-field="clientId" autocomplete="off" />
      </div>
      <div class="flex flex-col gap-1.5">
        <Label :for="`${credential.provider}-secret`">{{ copy('fields.clientSecret') }}</Label>
        <Input
          :id="`${credential.provider}-secret`"
          v-model="clientSecret"
          data-field="clientSecret"
          type="password"
          autocomplete="new-password"
        />
        <p class="text-sm text-muted-foreground">{{ copy('secretHint') }}</p>
      </div>
      <div v-if="isYouTube" class="flex flex-col gap-1.5">
        <Label :for="`${credential.provider}-api-key`">{{ copy('fields.apiKey') }}</Label>
        <Input
          :id="`${credential.provider}-api-key`"
          v-model="apiKey"
          data-field="apiKey"
          type="password"
          autocomplete="new-password"
        />
      </div>
      <div v-if="can('iam:manage')" class="flex flex-wrap items-center justify-end gap-2">
        <template v-if="confirmingClear">
          <span class="mr-auto text-sm text-muted-foreground">{{ copy('clearConfirmText') }}</span>
          <Button variant="ghost" @click="confirmingClear = false">{{ copy('cancel') }}</Button>
          <Button data-clear-confirm variant="destructive" :disabled="clear.isPending.value" @click="wipe">
            {{ copy('clearConfirm') }}
          </Button>
        </template>
        <template v-else>
          <Button data-clear variant="ghost" @click="confirmingClear = true">{{ copy('clear') }}</Button>
          <Button data-save :disabled="!canSave" @click="submit">
            {{ save.isPending.value ? copy('saving') : copy('save') }}
          </Button>
        </template>
      </div>
    </div>
  </li>
</template>
