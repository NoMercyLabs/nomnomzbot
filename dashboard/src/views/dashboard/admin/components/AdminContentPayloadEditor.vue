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
// the widget form.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import CodeEditor from '@/components/layout/CodeEditor.vue';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';

import AdminPipelinePayloadEditor from './AdminPipelinePayloadEditor.vue';
import AdminScriptPayloadEditor from './AdminScriptPayloadEditor.vue';
import { readPipelinePayload } from './pipelinePayload';
import AdminWidgetPayloadEditor from './AdminWidgetPayloadEditor.vue';
import { readScriptPayload } from './scriptPayload';
import { readWidgetPayload } from './widgetPayload';

const ENTRY = 'payload.json';

const props = defineProps<{
  title: string;
  // The kind of the definition: a pipeline draft opens in the step editor, every other kind in the JSON editor.
  kind: string;
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
