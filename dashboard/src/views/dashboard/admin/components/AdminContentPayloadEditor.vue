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
import { useI18n } from 'vue-i18n';

import CodeEditor from '@/components/layout/CodeEditor.vue';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';

const ENTRY = 'payload.json';

const props = defineProps<{
  title: string;
  initial: string;
  // Keeps a valid payload; resolves to the message the editor shows after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{ close: [] }>();

const { t } = useI18n();

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
  <CodeEditor
    :title="title"
    :entry="ENTRY"
    :files="{ [ENTRY]: initial }"
    language="json"
    :save="save"
    @close="emit('close')"
  />
</template>
