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
// A code script definition's draft in the channel script code editor (the same editor, completion and checks),
// so the operator edits the script, not JSON. Save hands the parent the payload in the shape the server compiles.
import { ref } from 'vue';

import { fetchScriptSdkTypes } from '@/api/codeScripts';
import CodeEditor from '@/components/layout/CodeEditor.vue';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';

import { SCRIPT_ENTRY, writeScriptPayload } from './scriptPayload';

const props = defineProps<{
  title: string;
  sourceCode: string;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{ close: [] }>();

// The editor is opened with its typed declarations, so it waits for them (they stay null when they cannot load).
const ready = ref(false);
const sdkTypes = ref<string | null>(null);

void fetchScriptSdkTypes()
  .catch(() => null)
  .then((types) => {
    sdkTypes.value = types;
    ready.value = true;
  });

async function save(files: EditorFiles): Promise<CompileFeedback> {
  const payloadJson = writeScriptPayload(files[SCRIPT_ENTRY] ?? '');

  return { ok: true, message: await props.keep(payloadJson) };
}
</script>

<template>
  <CodeEditor
    v-if="ready"
    :title="title"
    :entry="SCRIPT_ENTRY"
    :files="{ [SCRIPT_ENTRY]: sourceCode }"
    :sdk-types="sdkTypes ?? ''"
    :sdk-types-unavailable="sdkTypes === null"
    :save="save"
    @close="emit('close')"
  />
</template>
