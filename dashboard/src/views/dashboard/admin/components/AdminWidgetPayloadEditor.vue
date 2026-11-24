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
// A widget definition's draft in the channel's own parts: the events as the overlay list editor, and the source
// and the starting settings as two files in the channel code editor. The settings have no schema here (a platform
// widget declares none), so the overlay settings form cannot take them; they stay a JSON file in that editor.
// Save keeps the whole payload as a new version and is the one primary action; Edit JSON is the raw fallback.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { fetchWidgetSamples } from '@/api/widgetCode';
import CodeEditor from '@/components/layout/CodeEditor.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogFooter,
} from '@/components/ui/dialog';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';
import { ensureWords } from '@/i18n';
import type { JsonObject } from '@/lib/json';
import StringListEditor from '@/views/dashboard/overlays/components/StringListEditor.vue';

import {
  overlayWords,
  readSettingsText,
  settingsText,
  WIDGET_ENTRY,
  WIDGET_SETTINGS,
  writeWidgetPayload,
  type WidgetPayload,
} from './widgetPayload';
import AdminPayloadDialogHeader from './AdminPayloadDialogHeader.vue';

const props = defineProps<{
  title: string;
  payload: WidgetPayload;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();

const sourceCode = ref(props.payload.sourceCode);
const settings = ref<JsonObject>(props.payload.defaultSettings);
const events = ref(props.payload.defaultEventSubscriptions);

const wordsReady = ref(false);
const coding = ref(false);
const message = ref('');
const saving = ref(false);
const samples = ref<JsonObject>({});
const samplesError = ref<string>();

void ensureWords(overlayWords).then(() => {
  wordsReady.value = true;
});

// The fire bar sends the channel's sample events; a failed read still opens the editor, and the bar says so.
async function openCode(): Promise<void> {
  try {
    samples.value = await fetchWidgetSamples(channelId.value as string);
    samplesError.value = undefined;
  } catch {
    samplesError.value = t('dashboard.overlays.editor.samplesFailed');
  }

  coding.value = true;
}

function currentJson(): string {
  return writeWidgetPayload({
    sourceCode: sourceCode.value,
    defaultSettings: settings.value,
    defaultEventSubscriptions: events.value,
  });
}

async function save(): Promise<void> {
  saving.value = true;
  message.value = await props.keep(currentJson());
  saving.value = false;
}

// The code editor's Save keeps the source and the settings it holds together with the events of the form.
async function saveFiles(files: EditorFiles): Promise<CompileFeedback> {
  const edited = readSettingsText(files[WIDGET_SETTINGS] ?? '');

  if (edited === null)
    return { ok: false, message: t('dashboard.admin.content.widgetEditor.invalidSettings') };

  sourceCode.value = files[WIDGET_ENTRY] ?? '';
  settings.value = edited;

  return { ok: true, message: await props.keep(currentJson()) };
}
</script>

<template>
  <CodeEditor
    v-if="coding"
    :title="title"
    :entry="WIDGET_ENTRY"
    :files="{ [WIDGET_ENTRY]: sourceCode, [WIDGET_SETTINGS]: settingsText(settings) }"
    language="vue"
    :event-subscriptions="events"
    :fire-samples="samples"
    :fire-samples-error="samplesError"
    :save="saveFiles"
    @close="coding = false"
  />
  <Dialog v-else :open="true" @update:open="emit('close')">
    <DialogContent
      data-widget-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-xl"
    >
      <AdminPayloadDialogHeader
        :title="title"
        :description="$t('dashboard.admin.content.widgetEditor.description')"
      />
      <div v-if="wordsReady" class="flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto px-6 pb-4">
        <section class="grid gap-2">
          <h3 class="text-sm font-medium">
            {{ $t("dashboard.admin.content.widgetEditor.events") }}
          </h3>
          <p class="text-sm text-muted-foreground">
            {{ $t("dashboard.admin.content.widgetEditor.eventsHint") }}
          </p>
          <StringListEditor :items="events" @update="events = $event" />
        </section>
        <section class="grid gap-2">
          <h3 class="text-sm font-medium">
            {{ $t("dashboard.admin.content.widgetEditor.code") }}
          </h3>
          <p class="text-sm text-muted-foreground">
            {{ $t("dashboard.admin.content.widgetEditor.codeHint") }}
          </p>
          <Button
            data-widget-open-code
            type="button"
            variant="outline"
            class="w-fit"
            @click="openCode"
          >
            {{ $t("dashboard.admin.content.widgetEditor.openCode") }}
          </Button>
        </section>
      </div>
      <p
        v-if="message"
        data-widget-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-widget-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.widgetEditor.editJson") }}
        </Button>
        <Button
          data-widget-save
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
