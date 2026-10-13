<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one code editor (spec row Code editor): the server's own editor page (Monaco, file tree, problems,
// history, test run) in a full-window frame. The page brings its own top bar with Save & Compile and Close,
// so this component only speaks to it: it answers the page's requests through the callbacks below and emits
// close when the page asks to close. A request without its callback gets a visible failure from the bridge.
// Both sides require the dashboard's own origin; /editor is served through the same address as the dashboard.
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import {
  useEditorBridge,
  type CompileFeedback,
  type EditorFiles,
  type EditorHistoryRunner,
  type EditorOpenPayload,
  type EditorOutcome,
  type PreviewActionRequest,
  type PreviewActionResult,
  type TestRunRequest,
  type TestRunResult,
  type TestTrigger,
  type VersionsPage,
} from '@/composables/useEditorBridge'
import { useEditorLabels } from '@/composables/useEditorLabels'

const props = withDefaults(
  defineProps<{
    title: string
    files: EditorFiles
    entry: string
    // Hints the highlighting and the layout: 'script' has no preview pane, 'vue' is a widget.
    language?: string
    sdkTypes?: string
    sdkTypesUnavailable?: boolean
    testTriggers?: TestTrigger[]
    save: (files: EditorFiles) => Promise<CompileFeedback>
    // The first page of saved versions; with history, it makes the page show its History view.
    initialHistory?: VersionsPage
    history?: EditorHistoryRunner
    testRun?: (request: TestRunRequest) => Promise<EditorOutcome<TestRunResult>>
    previewAction?: (request: PreviewActionRequest) => Promise<PreviewActionResult>
  }>(),
  {
    language: 'script',
    sdkTypes: '',
    sdkTypesUnavailable: false,
    testTriggers: () => [],
    initialHistory: undefined,
    history: undefined,
    testRun: undefined,
    previewAction: undefined,
  },
)

const emit = defineEmits<{ close: [] }>()

const { t } = useI18n()
const frame: Readonly<Ref<HTMLIFrameElement | null>> = useTemplateRef<HTMLIFrameElement>('frame')
const labels = useEditorLabels(() => props.testRun !== undefined)

const payload = computed<EditorOpenPayload>(() => ({
  title: props.title,
  files: props.files,
  entry: props.entry,
  language: props.language,
  sdkTypes: props.sdkTypes,
  sdkTypesUnavailable: props.sdkTypesUnavailable,
  testTriggers: props.testTriggers,
  history: props.initialHistory,
  labels: labels.value,
}))

useEditorBridge({
  origin: window.location.origin,
  target: () => frame.value?.contentWindow ?? null,
  payload: () => payload.value,
  onSave: (files) => props.save(files),
  onClose: () => emit('close'),
  history: props.history,
  testRun: props.testRun ? (request) => props.testRun!(request) : undefined,
  previewAction: props.previewAction ? (request) => props.previewAction!(request) : undefined,
})

const opener: Ref<HTMLElement | null> = ref(null)
onMounted(() => {
  opener.value = document.activeElement instanceof HTMLElement ? document.activeElement : null
  frame.value?.focus()
})
onBeforeUnmount(() => opener.value?.focus())
</script>

<template>
  <div
    class="bg-background fixed inset-0 z-50"
    role="dialog"
    aria-modal="true"
    :aria-label="props.title"
  >
    <iframe
      ref="frame"
      class="size-full border-0"
      src="/editor/index.html"
      :title="t('codeEditor.frameTitle')"
    />
  </div>
</template>
