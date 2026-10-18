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

import type { OutboundWebhook, OutboundWebhookDraft, WebhookEventEntry } from '@/api/webhooks';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';

const props = defineProps<{
  endpoint: OutboundWebhook | null;
  catalog: WebhookEventEntry[];
  pending: boolean;
  error?: string;
}>();

const emit = defineEmits<{ submit: [draft: OutboundWebhookDraft] }>();
const open = defineModel<boolean>('open', { required: true });

const name = ref('');
const fqdn = ref('');
const path = ref('');
const events = ref<string[]>([]);

const saved = computed(() => props.endpoint !== null);

watch(open, (isOpen) => {
  if (!isOpen) return;

  name.value = props.endpoint?.name ?? '';
  fqdn.value = props.endpoint?.fqdn ?? '';
  path.value = props.endpoint?.path ?? '';
  events.value = props.endpoint?.subscribedEventTypes.slice() ?? [];
});

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
    isEnabled: props.endpoint?.isEnabled ?? true,
  });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-dialog="outbound-webhook"
    :title="saved ? $t('dashboard.settings.webhooks.outbound.editTitle') : $t('dashboard.settings.webhooks.outbound.createTitle')"
    :description="$t('dashboard.settings.webhooks.outbound.formDescription')"
    :action="saved ? $t('dashboard.settings.webhooks.outbound.save') : $t('dashboard.settings.webhooks.outbound.create')"
    :pending="pending"
    :error="error"
    @submit="submit"
  >
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.webhooks.outbound.name') }}
      <Input v-model="name" data-field="name" maxlength="100" />
    </label>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.webhooks.outbound.host') }}
      <Input v-model="fqdn"
             data-field="host"
             class="font-mono"
             :disabled="saved"
             placeholder="hooks.example.com"
      />
      <span class="text-sm font-normal text-muted-foreground">
        {{ $t('dashboard.settings.webhooks.outbound.hostHelp') }}
      </span>
    </label>
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.settings.webhooks.outbound.path') }}
      <Input v-model="path"
             data-field="path"
             class="font-mono"
             :disabled="saved"
             placeholder="/nomnomzbot"
      />
    </label>
    <fieldset class="grid gap-2">
      <legend class="text-sm font-medium">{{ $t('dashboard.settings.webhooks.outbound.eventsTitle') }}</legend>
      <label v-for="entry in catalog" :key="entry.eventType" class="flex items-center gap-2 text-sm">
        <Checkbox
          :data-event="entry.eventType"
          :model-value="events.includes(entry.eventType)"
          @update:model-value="tick(entry.eventType, $event === true)"
        />
        {{ entry.label }}
      </label>
    </fieldset>
  </FormDialog>
</template>
