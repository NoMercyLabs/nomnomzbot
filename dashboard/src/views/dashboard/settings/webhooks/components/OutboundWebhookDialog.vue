<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Makes or edits one outbound webhook: the name, the address (fixed once made) and the events it follows.
// The events come from the bot's catalog, so the streamer ticks them and types nothing blind.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { OutboundWebhook, OutboundWebhookDraft, WebhookEventEntry } from '@/api/webhooks';
import FormDialog from '@/components/layout/FormDialog.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { parseHeaderLines } from '@/lib/text';

const props = defineProps<{
  endpoint: OutboundWebhook | null;
  catalog: WebhookEventEntry[];
  pending: boolean;
  error?: string;
}>();

const { t } = useI18n();

const emit = defineEmits<{ submit: [draft: OutboundWebhookDraft] }>();
const open = defineModel<boolean>('open', { required: true });

const name = ref('');
const fqdn = ref('');
const path = ref('');
const events = ref<string[]>([]);
const bodyTemplate = ref('');
const bodyIsJson = ref(true);
const headerText = ref('');

const saved = computed(() => props.endpoint !== null);

// The catalog is long, so the events sit under their category heading, in the order the catalog lists them.
const groups = computed(() => {
  const byCategory = new Map<string, WebhookEventEntry[]>();

  for (const entry of props.catalog) {
    const entries = byCategory.get(entry.categoryKey) ?? [];

    entries.push(entry);
    byCategory.set(entry.categoryKey, entries);
  }

  return Array.from(byCategory, ([category, entries]) => ({ category, entries }));
});
const parsedHeaders = computed(() => parseHeaderLines(headerText.value));
const invalidHeaders = computed(() => parsedHeaders.value.invalid.length > 0);

const headersNote = computed(() => {
  if (invalidHeaders.value)
    return t('dashboard.settings.webhooks.outbound.headersInvalid', {
      line: parsedHeaders.value.invalid[0],
    });

  return t(
    saved.value
      ? 'dashboard.settings.webhooks.outbound.headersKept'
      : 'dashboard.settings.webhooks.outbound.headersHelp',
  );
});

watch(open, (isOpen) => {
  if (!isOpen) return;

  name.value = props.endpoint?.name ?? '';
  fqdn.value = props.endpoint?.fqdn ?? '';
  path.value = props.endpoint?.path ?? '';
  events.value = props.endpoint?.subscribedEventTypes.slice() ?? [];
  bodyTemplate.value = props.endpoint?.bodyTemplate ?? '';
  bodyIsJson.value = props.endpoint?.bodyIsJson ?? true;
  headerText.value = '';
});

// An empty template is null (use the default body). On an edit, a template that was there and is now empty
// goes as an empty string, which the bot reads as a clear; null would keep the old one.
function templateToSend(): string | null {
  if (bodyTemplate.value.trim() !== '')
    return bodyTemplate.value;

  return props.endpoint?.bodyTemplate
    ? ''
    : null;
}

// Stored headers are never sent back to the page, so an empty box keeps them (null) instead of erasing them.
function headersToSend(): Record<string, string> | null {
  return Object.keys(parsedHeaders.value.headers).length === 0
    ? null
    : parsedHeaders.value.headers;
}

function tick(eventType: string, on: boolean): void {
  events.value = on
    ? events.value.concat(eventType)
    : events.value.filter((kept) => kept !== eventType);
}

function submit(): void {
  const trimmedPath = path.value.trim();

  emit('submit', {
    name: name.value.trim(),
    fqdn: fqdn.value.trim(),
    path: trimmedPath === ''
      ? null
      : trimmedPath,
    subscribedEventTypes: events.value,
    bodyTemplate: templateToSend(),
    bodyIsJson: bodyIsJson.value,
    customHeaders: headersToSend(),
    isEnabled: props.endpoint?.isEnabled ?? true,
  });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-dialog="outbound-webhook"
    :title="
      saved
        ? $t('dashboard.settings.webhooks.outbound.editTitle')
        : $t('dashboard.settings.webhooks.outbound.createTitle')
    "
    :description="$t('dashboard.settings.webhooks.outbound.formDescription')"
    :action="
      saved
        ? $t('dashboard.settings.webhooks.outbound.save')
        : $t('dashboard.settings.webhooks.outbound.create')
    "
    :pending="pending"
    :error="error"
    :disabled="invalidHeaders"
    @submit="submit"
  >
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.outbound.name") }}
      <Input v-model="name" data-field="name" maxlength="100" />
    </label>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.outbound.host") }}
      <Input
        v-model="fqdn"
        data-field="host"
        class="font-mono"
        :disabled="saved"
        placeholder="hooks.example.com"
      />
      <span class="text-sm font-normal text-muted-foreground">
        {{ $t("dashboard.settings.webhooks.outbound.hostHelp") }}
      </span>
    </label>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.outbound.path") }}
      <Input
        v-model="path"
        data-field="path"
        class="font-mono"
        :disabled="saved"
        placeholder="/nomnomzbot"
      />
    </label>
    <fieldset class="grid gap-2">
      <legend class="text-sm font-medium">
        {{ $t("dashboard.settings.webhooks.outbound.eventsTitle") }}
      </legend>
      <div
        v-for="group in groups"
        :key="group.category"
        :data-event-group="group.category"
        class="grid gap-2"
      >
        <span class="text-sm font-medium text-muted-foreground">{{ $t(group.category) }}</span>
        <div class="grid gap-x-4 gap-y-2 sm:grid-cols-2">
          <label
            v-for="entry in group.entries"
            :key="entry.eventType"
            class="flex items-center gap-2 text-sm"
          >
            <Checkbox
              :data-event="entry.eventType"
              :model-value="events.includes(entry.eventType)"
              @update:model-value="tick(entry.eventType, $event === true)"
            />
            {{ $t(entry.labelKey) }}
          </label>
        </div>
      </div>
    </fieldset>
    <TemplateField
      v-model="bodyTemplate"
      context="webhook"
      data-field="body-template"
      :label="$t('dashboard.settings.webhooks.outbound.bodyTemplate')"
      :help="$t('dashboard.settings.webhooks.outbound.bodyTemplateHelp')"
      :rows="4"
    />
    <LabeledSwitch
      v-model="bodyIsJson"
      data-field="body-is-json"
      :label="$t('dashboard.settings.webhooks.outbound.bodyIsJson')"
      :help="$t('dashboard.settings.webhooks.outbound.bodyIsJsonHelp')"
    />
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t("dashboard.settings.webhooks.outbound.headers") }}
      <Textarea
        v-model="headerText"
        data-field="headers"
        class="font-mono"
        rows="3"
        placeholder="X-Token: value"
        :aria-invalid="invalidHeaders"
      />
      <span
        class="text-sm font-normal"
        :class="invalidHeaders ? 'text-destructive' : 'text-muted-foreground'"
      >
        {{ headersNote }}
      </span>
    </label>
  </FormDialog>
</template>
