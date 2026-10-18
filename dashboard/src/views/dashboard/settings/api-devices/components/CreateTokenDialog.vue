<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Makes one key: a name, what it may do (scopes), optionally which pipelines it may start, and an expiry day.
// The finished key shows once in the secret dialog; this dialog only hands the request up.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { automationScopes, type NewAutomationToken } from '@/api/automationTokens';
import { usePipelines } from '@/api/pipelines';
import FormDialog from '@/components/layout/FormDialog.vue';
import MultiChoice from '@/components/layout/MultiChoice.vue';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

defineProps<{ pending: boolean; error?: string }>();
const emit = defineEmits<{ submit: [request: NewAutomationToken] }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const pipelines = usePipelines(channelId);

const name = ref('');
const scopes = ref<string[]>(['invoke', 'read', 'events']);
const pipelineIds = ref<string[]>([]);
const expiresOn = ref('');

watch(open, (shown) => {
  if (!shown) return;

  name.value = '';
  scopes.value = ['invoke', 'read', 'events'];
  pipelineIds.value = [];
  expiresOn.value = '';
});

const scopeOptions = computed(() =>
  automationScopes.map((scope) => ({ value: scope, label: t(`dashboard.settings.apiDevices.scopes.${scope}`) })),
);

const pipelineOptions = computed(() =>
  (pipelines.data.value ?? []).map((row) => ({ value: row.id, label: row.name })),
);

const canPin = computed(() => scopes.value.includes('invoke'));

function submit(): void {
  if (name.value.trim() === '' || scopes.value.length === 0) return;

  emit('submit', {
    name: name.value.trim(),
    scopes: scopes.value,
    allowedPipelineIds: canPin.value && pipelineIds.value.length > 0
      ? pipelineIds.value
      : null,
    expiresAt: expiresOn.value === ''
      ? null
      : new Date(`${expiresOn.value}T23:59:59`).toISOString(),
  });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-dialog="create-token"
    :title="$t('dashboard.settings.apiDevices.create.title')"
    :description="$t('dashboard.settings.apiDevices.create.description')"
    :action="$t('dashboard.settings.apiDevices.create.action')"
    :pending="pending"
    :error="error"
    @submit="submit"
  >
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.apiDevices.create.name') }}
      <Input
        v-model="name"
        data-field="name"
        maxlength="100"
        :placeholder="$t('dashboard.settings.apiDevices.create.namePlaceholder')"
      />
    </label>
    <div class="grid gap-1.5">
      <span class="text-sm font-medium">{{ $t('dashboard.settings.apiDevices.create.scopes') }}</span>
      <MultiChoice v-model="scopes" data-field="scopes" :options="scopeOptions" />
      <span class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.apiDevices.create.scopesHelp') }}
      </span>
    </div>
    <div v-if="canPin && pipelineOptions.length > 0" class="grid gap-1.5">
      <span class="text-sm font-medium">{{ $t('dashboard.settings.apiDevices.create.pipelines') }}</span>
      <MultiChoice v-model="pipelineIds" data-field="pipelines" :options="pipelineOptions" />
      <span class="text-sm text-muted-foreground">
        {{ $t('dashboard.settings.apiDevices.create.pipelinesHelp') }}
      </span>
    </div>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.apiDevices.create.expires') }}
      <Input v-model="expiresOn" data-field="expires" type="date" />
      <span class="text-sm font-normal text-muted-foreground">
        {{ $t('dashboard.settings.apiDevices.create.expiresHelp') }}
      </span>
    </label>
  </FormDialog>
</template>
