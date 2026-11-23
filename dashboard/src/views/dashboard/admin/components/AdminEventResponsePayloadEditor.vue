<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// An event response definition's draft with the fields of the channel's event response editor: the line the bot
// posts, what answers (a chat message, the overlay, a pipeline or nothing) and the Enabled switch. The event is
// the fixed key of the row. Save keeps the whole payload as a new version and is the one primary action; Edit JSON
// is the raw fallback.
import Cross from '~icons/nnz/cross';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useEventPresets } from '@/api/eventResponses';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { ensureWords } from '@/i18n';
import { useEventName } from '@/views/dashboard/event-responses/components/useEventName';

import {
  eventResponseWords,
  responseTypes,
  writeEventResponsePayload,
  type EventResponsePayload,
  type ResponseType,
} from './eventResponsePayload';

const props = defineProps<{
  title: string;
  payload: EventResponsePayload;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { t } = useI18n();
const eventName = useEventName();
const { channelId } = useCurrentChannel();
const presets = useEventPresets(channelId);

const responseType = ref<ResponseType>(props.payload.responseType);
const text = ref(props.payload.message);
const enabled = ref(props.payload.isEnabled);

const wordsReady = ref(false);
const message = ref('');
const saving = ref(false);

void ensureWords(eventResponseWords).then(() => {
  wordsReady.value = true;
});

const variables = computed(() => {
  const preset = presets.data.value?.find((item) => item.eventType === props.payload.eventType);
  const names = (preset?.variables ?? []).map((name) => `{${name}}`).join(' ');

  return names === ''
    ? undefined
    : t('dashboard.eventResponses.editor.variables', { names });
});

function currentJson(): string {
  return writeEventResponsePayload({
    eventType: props.payload.eventType,
    responseType: responseType.value,
    message: text.value,
    metadata: props.payload.metadata,
    isEnabled: enabled.value,
  });
}

async function save(): Promise<void> {
  saving.value = true;
  message.value = await props.keep(currentJson());
  saving.value = false;
}
</script>

<template>
  <Dialog :open="true" @update:open="emit('close')">
    <DialogContent
      data-event-response-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-xl"
    >
      <div class="flex flex-none items-start justify-between gap-4 px-6 pt-6 pb-4">
        <div class="grid gap-1.5">
          <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
          <DialogDescription class="text-sm text-muted-foreground">
            {{ $t("dashboard.admin.content.eventResponseEditor.description") }}
          </DialogDescription>
        </div>
        <DialogClose as-child>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('common.close')"
          >
            <Cross class="size-5" aria-hidden="true" />
          </Button>
        </DialogClose>
      </div>
      <div v-if="wordsReady" class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-6 pb-4">
        <p class="text-sm text-muted-foreground">
          {{ $t("dashboard.admin.content.eventResponseEditor.event") }}
          <span class="font-medium text-foreground">{{ eventName(payload.eventType) }}</span>
        </p>

        <Field>
          <FieldLabel>{{ $t("dashboard.admin.content.eventResponseEditor.responseType") }}</FieldLabel>
          <Select v-model="responseType">
            <SelectTrigger
              :aria-label="$t('dashboard.admin.content.eventResponseEditor.responseType')"
              class="w-full"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="type in responseTypes" :key="type" :value="type">
                {{ $t(`dashboard.admin.content.eventResponseEditor.types.${type}`) }}
              </SelectItem>
            </SelectContent>
          </Select>
        </Field>

        <TemplateField
          v-model="text"
          :label="$t('dashboard.eventResponses.editor.message')"
          :help="variables"
          :panel="false"
          context="eventResponse"
          :maxlength="500"
          :event-type="payload.eventType"
        />

        <LabeledSwitch
          v-model="enabled"
          :label="$t('dashboard.admin.content.eventResponseEditor.enabled')"
        />
      </div>
      <p
        v-if="message"
        data-event-response-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-event-response-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.eventResponseEditor.editJson") }}
        </Button>
        <Button
          data-event-response-save
          type="button"
          :disabled="saving"
          @click="save"
        >
          {{ $t("dashboard.admin.content.draft.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
