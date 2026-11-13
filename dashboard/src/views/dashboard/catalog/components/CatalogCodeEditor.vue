<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The code editor opens on the channel's first real script. Reading only: nothing saves until someone presses Save in it.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchScriptProject,
  fetchScriptSdkTypes,
  saveScriptProject,
  useCodeScripts,
  type ScriptProject,
} from '@/api/codeScripts';
import CodeEditor from '@/components/layout/CodeEditor.vue';
import { Button } from '@/components/ui/button';
import type { CompileFeedback, EditorFiles } from '@/composables/useEditorBridge';
import { errorText } from '@/lib/errors';

const { t } = useI18n();

type EditorSession = {
  id: string;
  name: string;
  project: ScriptProject;
  sdkTypes: string | null;
};
const scripts = useCodeScripts();
const editorSession = ref<EditorSession | null>(null);
const editorMissing = ref(false);

async function openEditor(): Promise<void> {
  const first = scripts.data.value?.[0];

  editorMissing.value = first === undefined;

  if (!first) return;

  const project = await fetchScriptProject(first.id);

  if (!project) return;

  editorSession.value = {
    id: first.id,
    name: first.name,
    project,
    sdkTypes: await fetchScriptSdkTypes(first.id),
  };
}

async function saveEditor(files: EditorFiles): Promise<CompileFeedback> {
  const session = editorSession.value;

  if (!session)
    return { ok: false, message: '' };

  return saveScriptProject(session.id, { ...session.project, files });
}
</script>

<template>
  <section data-catalog="code-editor" class="grid gap-4">
    <div class="flex items-center gap-4">
      <Button variant="outline" @click="openEditor">{{
        t('dashboard.catalog.codeEditor.open')
      }}</Button>
      <span v-if="scripts.error.value" role="alert" class="text-sm text-destructive">
        {{ errorText(scripts.error.value, t) }}
      </span>
      <span v-else-if="editorMissing" class="text-sm text-muted-foreground">
        {{ t('dashboard.catalog.codeEditor.none') }}
      </span>
    </div>
    <CodeEditor
      v-if="editorSession"
      :title="editorSession.name"
      :files="editorSession.project.files"
      :entry="editorSession.project.manifest.entry"
      :sdk-types="editorSession.sdkTypes ?? ''"
      :sdk-types-unavailable="editorSession.sdkTypes === null"
      :save="saveEditor"
      @close="editorSession = null"
    />
  </section>
</template>
