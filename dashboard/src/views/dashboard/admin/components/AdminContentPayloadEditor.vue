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
// A content payload is JSON, so it is written in the project's Monaco editor, never in a plain text box.
// The editor shows one file. Its Save checks that the text is JSON before the parent keeps it.
// A pipeline draft opens in the step editor, a code script draft in the script code editor and a widget draft in
// the widget form, a command draft in the built-in command fields, an event response draft in the event
// response fields, a timer draft in the timer fields, a reward draft in the reward fields and a pick list draft
// in the pick list fields.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import CodeEditor from '@/components/layout/CodeEditor.vue';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';

import AdminCommandPayloadEditor from './AdminCommandPayloadEditor.vue';
import AdminPipelinePayloadEditor from './AdminPipelinePayloadEditor.vue';
import AdminEventResponsePayloadEditor from './AdminEventResponsePayloadEditor.vue';
import { readCommandPayload } from './commandPayload';
import { readEventResponsePayload } from './eventResponsePayload';
import AdminPickListPayloadEditor from './AdminPickListPayloadEditor.vue';
import AdminRewardPayloadEditor from './AdminRewardPayloadEditor.vue';
import AdminScriptPayloadEditor from './AdminScriptPayloadEditor.vue';
import { readPickListPayload } from './pickListPayload';
import { readPipelinePayload } from './pipelinePayload';
import { readRewardPayload } from './rewardPayload';
import AdminTimerPayloadEditor from './AdminTimerPayloadEditor.vue';
import AdminWidgetPayloadEditor from './AdminWidgetPayloadEditor.vue';
import { readScriptPayload } from './scriptPayload';
import { readTimerPayload } from './timerPayload';
import { readWidgetPayload } from './widgetPayload';

const ENTRY = 'payload.json';

const props = defineProps<{
  title: string;
  // The kind of the definition: a pipeline draft opens in the step editor, every other kind in the JSON editor.
  kind: string;
  // The key of the definition: a command draft shows the defaults of the built-in with this key.
  commandKey: string;
  initial: string;
  // Keeps a valid payload; resolves to the message the editor shows after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{ close: [] }>();

const { t } = useI18n();

// The steps of a pipeline payload, or null: another kind, or a payload the step editor would not hold whole.
const graph = computed(() =>
  props.kind === 'pipeline'
    ? readPipelinePayload(props.initial)
    : null);
// The source of a code script payload, or null: another kind, or a payload the code editor would not hold whole.
const scriptSource = computed(() =>
  props.kind === 'code_script'
    ? readScriptPayload(props.initial)
    : null);
// The parts of a widget payload, or null: another kind, or a payload the widget form would not hold whole.
const widget = computed(() =>
  props.kind === 'widget'
    ? readWidgetPayload(props.initial)
    : null);
// The overrides of a command payload, or null: another kind, or a payload the command form would not hold whole.
const command = computed(() =>
  props.kind === 'command'
    ? readCommandPayload(props.initial)
    : null);
// The parts of an event response payload, or null: another kind, or a payload the form would not hold whole.
const eventResponse = computed(() =>
  props.kind === 'event_response'
    ? readEventResponsePayload(props.initial)
    : null);
// The parts of a timer payload, or null: another kind, or a payload the form would not hold whole.
const timer = computed(() =>
  props.kind === 'timer'
    ? readTimerPayload(props.initial)
    : null);
// The parts of a reward payload, or null: another kind, or a payload the form would not hold whole.
const reward = computed(() =>
  props.kind === 'reward'
    ? readRewardPayload(props.initial)
    : null);
// The parts of a pick list payload, or null: another kind, or a payload the form would not hold whole.
const pickList = computed(() =>
  props.kind === 'pick_list'
    ? readPickListPayload(props.initial)
    : null);
// Set when the operator leaves a form: the raw editor then starts from the draft as it stood.
const rawSeed = ref<string | null>(null);

function isJson(value: string): boolean {
  try {
    JSON.parse(value);

    return true;
  } catch {
    return false;
  }
}

async function save(files: EditorFiles): Promise<CompileFeedback> {
  const payloadJson = files[ENTRY] ?? '';

  if (!isJson(payloadJson))
    return { ok: false, message: t('dashboard.admin.content.invalidPayload') };

  return { ok: true, message: await props.keep(payloadJson) };
}
</script>

<template>
  <AdminPipelinePayloadEditor
    v-if="graph && rawSeed === null"
    :title="title"
    :graph="graph"
    :keep="keep"
    @edit-json="rawSeed = $event"
    @close="emit('close')"
  />
  <AdminScriptPayloadEditor
    v-else-if="scriptSource !== null"
    :title="title"
    :source-code="scriptSource"
    :keep="keep"
    @close="emit('close')"
  />
  <AdminWidgetPayloadEditor
    v-else-if="widget && rawSeed === null"
    :title="title"
    :payload="widget"
    :keep="keep"
    @edit-json="rawSeed = $event"
    @close="emit('close')"
  />
  <AdminCommandPayloadEditor
    v-else-if="command && rawSeed === null"
    :title="title"
    :command-key="commandKey"
    :payload="command"
    :keep="keep"
    @edit-json="rawSeed = $event"
    @close="emit('close')"
  />
  <AdminEventResponsePayloadEditor
    v-else-if="eventResponse && rawSeed === null"
    :title="title"
    :payload="eventResponse"
    :keep="keep"
    @edit-json="rawSeed = $event"
    @close="emit('close')"
  />
  <AdminTimerPayloadEditor
    v-else-if="timer && rawSeed === null"
    :title="title"
    :payload="timer"
    :keep="keep"
    @edit-json="rawSeed = $event"
    @close="emit('close')"
  />
  <AdminRewardPayloadEditor
    v-else-if="reward && rawSeed === null"
    :title="title"
    :payload="reward"
    :keep="keep"
    @edit-json="rawSeed = $event"
    @close="emit('close')"
  />
  <AdminPickListPayloadEditor
    v-else-if="pickList && rawSeed === null"
    :title="title"
    :payload="pickList"
    :keep="keep"
    @edit-json="rawSeed = $event"
    @close="emit('close')"
  />
  <CodeEditor
    v-else
    :title="title"
    :entry="ENTRY"
    :files="{ [ENTRY]: rawSeed ?? initial }"
    language="json"
    :save="save"
    @close="emit('close')"
  />
</template>
