<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Makes or edits one inbound webhook: the name, the provider kind (fixed once made), the secret that proves a call
// is real, what the call starts (a pipeline or an event) and, for a generic provider, where its signature and
// event fields sit. The secret is never read back: an empty box on an edit keeps the stored one.
import { computed, ref, watch } from 'vue';

import type { PipelineListItem } from '@/api/pipelines';
import type { InboundWebhook, InboundWebhookDraft } from '@/api/webhooks';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

type Adapter = InboundWebhookDraft['adapter'];

const props = defineProps<{
  endpoint: InboundWebhook | null;
  pipelines: PipelineListItem[];
  pending: boolean;
  error?: string;
}>();

const emit = defineEmits<{ submit: [draft: InboundWebhookDraft] }>();
const open = defineModel<boolean>('open', { required: true });

const adapters = [
  'Generic',
  'Kofi',
  'Github',
  'Fourthwall',
  'Shopify',
  'Patreon',
  'Buymeacoffee',
] as const satisfies readonly Adapter[];

// The select cannot hold an empty value, so "no pipeline" is a word that no pipeline id can be.
const noPipeline = '__none';

const name = ref('');
const adapter = ref<Adapter>('Generic');
const secret = ref('');
const pipelineId = ref(noPipeline);
const eventType = ref('');
const signatureHeader = ref('');
const signaturePrefix = ref('');
const signingString = ref('');
const timestampHeader = ref('');
const bodyField = ref('');
const kindPath = ref('');
const idPath = ref('');

const saved = computed(() => props.endpoint !== null);
const generic = computed(() => adapter.value === 'Generic');

const ready = computed(
  () =>
    name.value.trim() !== '' &&
    (saved.value || secret.value !== '') &&
    (!generic.value || (kindPath.value.trim() !== '' && idPath.value.trim() !== '')),
);

watch(open, (isOpen) => {
  if (!isOpen) return;

  const config = props.endpoint?.genericConfig;

  name.value = props.endpoint?.name ?? '';
  adapter.value = props.endpoint?.adapter ?? 'Generic';
  secret.value = '';
  pipelineId.value = props.endpoint?.targetPipelineId ?? noPipeline;
  eventType.value = props.endpoint?.targetEventType ?? '';
  signatureHeader.value = config?.signatureHeaderName ?? '';
  signaturePrefix.value = config?.signaturePrefix ?? '';
  signingString.value = config?.signingStringTemplate ?? '';
  timestampHeader.value = config?.timestampHeaderName ?? '';
  bodyField.value = config?.sharedSecretBodyField ?? '';
  kindPath.value = config?.eventKindJsonPath ?? '';
  idPath.value = config?.providerEventIdJsonPath ?? '';
});

function orNull(value: string): string | null {
  const trimmed = value.trim();

  return trimmed === ''
    ? null
    : trimmed;
}

function genericConfig(): InboundWebhookDraft['genericConfig'] {
  if (!generic.value)
    return null;

  return {
    signatureHeaderName: orNull(signatureHeader.value),
    signaturePrefix: orNull(signaturePrefix.value),
    signingStringTemplate: orNull(signingString.value),
    timestampHeaderName: orNull(timestampHeader.value),
    sharedSecretBodyField: orNull(bodyField.value),
    eventKindJsonPath: kindPath.value.trim(),
    providerEventIdJsonPath: idPath.value.trim(),
  };
}

function submit(): void {
  emit('submit', {
    name: name.value.trim(),
    adapter: adapter.value,
    verificationSecret: secret.value,
    targetPipelineId: pipelineId.value === noPipeline
      ? null
      : pipelineId.value,
    targetEventType: orNull(eventType.value),
    genericConfig: genericConfig(),
    isEnabled: props.endpoint?.isEnabled ?? true,
  });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-dialog="inbound-webhook"
    :title="
      saved
        ? $t('dashboard.settings.webhooks.inbound.editTitle')
        : $t('dashboard.settings.webhooks.inbound.createTitle')
    "
    :description="$t('dashboard.settings.webhooks.inbound.formDescription')"
    :action="
      saved
        ? $t('dashboard.settings.webhooks.inbound.save')
        : $t('dashboard.settings.webhooks.inbound.create')
    "
    :pending="pending"
    :error="error"
    :disabled="!ready"
    @submit="submit"
  >
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.inbound.name") }}
      <Input v-model="name" data-field="inbound-name" maxlength="100" />
    </label>
    <div class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.inbound.adapter") }}
      <Select v-model="adapter" :disabled="saved">
        <SelectTrigger class="w-full" data-field="inbound-adapter">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="kind in adapters" :key="kind" :value="kind">{{ kind }}</SelectItem>
        </SelectContent>
      </Select>
    </div>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.inbound.secret") }}
      <Input v-model="secret" data-field="inbound-secret" type="password" autocomplete="off" />
      <span class="text-sm font-normal text-muted-foreground">
        {{
          saved
            ? $t("dashboard.settings.webhooks.inbound.secretKept")
            : $t("dashboard.settings.webhooks.inbound.secretHelp")
        }}
      </span>
    </label>
    <div class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.inbound.pipeline") }}
      <Select v-model="pipelineId">
        <SelectTrigger class="w-full" data-field="inbound-pipeline">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="noPipeline">{{
            $t("dashboard.settings.webhooks.inbound.noPipeline")
          }}</SelectItem>
          <SelectItem v-for="pipeline in pipelines" :key="pipeline.id" :value="pipeline.id">
            {{ pipeline.name }}
          </SelectItem>
        </SelectContent>
      </Select>
    </div>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.inbound.eventType") }}
      <Input v-model="eventType" data-field="inbound-event-type" class="font-mono" />
      <span class="text-sm font-normal text-muted-foreground">
        {{ $t("dashboard.settings.webhooks.inbound.eventTypeHelp") }}
      </span>
    </label>
    <div v-if="generic" class="grid gap-4 sm:grid-cols-2">
      <label class="grid gap-1.5 text-sm font-medium">
        {{ $t("dashboard.settings.webhooks.inbound.signatureHeader") }}
        <Input
          v-model="signatureHeader"
          data-field="signature-header"
          class="font-mono"
          placeholder="X-Signature"
        />
      </label>
      <label class="grid gap-1.5 text-sm font-medium">
        {{ $t("dashboard.settings.webhooks.inbound.signaturePrefix") }}
        <Input
          v-model="signaturePrefix"
          data-field="signature-prefix"
          class="font-mono"
          placeholder="sha256="
        />
      </label>
      <label class="grid gap-1.5 text-sm font-medium sm:col-span-2">
        {{ $t("dashboard.settings.webhooks.inbound.signingString") }}
        <Input v-model="signingString" data-field="signing-string" class="font-mono" />
      </label>
      <label class="grid gap-1.5 text-sm font-medium">
        {{ $t("dashboard.settings.webhooks.inbound.timestampHeader") }}
        <Input v-model="timestampHeader" data-field="timestamp-header" class="font-mono" />
      </label>
      <label class="grid gap-1.5 text-sm font-medium">
        {{ $t("dashboard.settings.webhooks.inbound.bodyField") }}
        <Input v-model="bodyField" data-field="body-secret-field" class="font-mono" />
      </label>
      <label class="grid gap-1.5 text-sm font-medium">
        {{ $t("dashboard.settings.webhooks.inbound.kindPath") }}
        <Input
          v-model="kindPath"
          data-field="event-kind-path"
          class="font-mono"
          placeholder="$.type"
        />
      </label>
      <label class="grid gap-1.5 text-sm font-medium">
        {{ $t("dashboard.settings.webhooks.inbound.idPath") }}
        <Input v-model="idPath" data-field="event-id-path" class="font-mono" placeholder="$.id" />
      </label>
    </div>
  </FormDialog>
</template>
