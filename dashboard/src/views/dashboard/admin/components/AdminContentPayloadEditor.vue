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
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import CodeEditor from '@/components/layout/CodeEditor.vue';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';

import AdminPipelinePayloadEditor from './AdminPipelinePayloadEditor.vue';
import { readPipelinePayload } from './pipelinePayload';

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
// Set when the operator leaves the step editor: the raw editor then starts from the steps as they stood.
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
