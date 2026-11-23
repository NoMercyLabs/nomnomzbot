<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The reaction section of every responder: one switch between a simple reply and Actions. The simple reply
// is the default slot. Actions shows the whole step palette in place, so any action type is one press away;
// the first pick creates a pipeline with that step, binds it and then shows its steps for editing.
// A link keeps the way to an existing pipeline or command (the shared target picker).
// Every responder gets the same options: no editor passes its own list of kinds.
// An editor with more than one kind of simple reply (text, random) passes them as simpleOptions; they sit in
// the same one switch as Actions.
import { useElementSize } from '@vueuse/core';
import { computed, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useChannelAccess } from '@/api/access';
import {
  useCreatePipeline,
  usePipelineCatalog,
  useSavePipeline,
  type PipelineStep,
} from '@/api/pipelines';
import type { ReplyMode } from '@/components/layout/replyMode';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import TargetPicker from '@/components/layout/TargetPicker.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { ensureWords, schemaWords } from '@/i18n';
import type { PickedTarget, TargetKind } from '@/lib/bindingTarget';
import { errorText } from '@/lib/errors';
import { newId } from '@/lib/id';
import { newAction, newBlock, type AddableBlock } from '@/views/dashboard/pipelines/components/newSteps';
import StepPalette from '@/views/dashboard/pipelines/components/StepPalette.vue';
import { pipelineWords } from '@/views/dashboard/pipelines/components/stepWords';

type Option = { value: string; label: string };

const mode = defineModel<ReplyMode>('mode', { required: true });
const target = defineModel<PickedTarget | null>('target', { default: null });
const simpleKind = defineModel<string>('simpleKind', { default: '' });

// A locked editor shows the setting but lets no one change it.
const props = withDefaults(
  defineProps<{
    disabled?: boolean;
    simpleOptions?: readonly Option[];
    newPipelineName?: string;
  }>(),
  {
    disabled: false,
    simpleOptions: () => [],
    newPipelineName: '',
  },
);

const FULL = 'full';
const kinds = ['command', 'pipeline', 'action'] as const satisfies readonly TargetKind[];

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const catalog = usePipelineCatalog(channelId);
const create = useCreatePipeline(channelId);
const save = useSavePipeline(channelId);
const access = useChannelAccess(channelId);
const locked = computed<boolean>(() => props.disabled || !access.can('pipelines:write'));

// The palette titles are words of the pipelines pages; a form elsewhere loads them itself. The translation
// lookup does not notify a computed, so the words arriving flips this flag.
const wordsReady = ref(false);

void Promise.all([
  ensureWords(pipelineWords),
  ensureWords(schemaWords),
]).then(() => {
  wordsReady.value = true;
});

// Every mode keeps the tallest height any mode has had (text, random reply, pipeline), so a switch never
// moves what sits below (owner 2026-10-17: "its a huge layout shift").
const simpleBox = useTemplateRef<HTMLElement>('simpleBox');
const { height: simpleHeight } = useElementSize(simpleBox, undefined, { box: 'border-box' });
const reservedHeight = ref(0);

watch(simpleHeight, (height: number): void => {
  reservedHeight.value = Math.max(reservedHeight.value, height);
});

const simple = computed<readonly Option[]>(() =>
  props.simpleOptions.length > 0
    ? props.simpleOptions
    : [{ value: 'simple', label: t('common.replyMode.simple') }],
);
const options = computed<Option[]>(() => [
  ...simple.value,
  { value: FULL, label: t('common.replyMode.full') },
]);

const choice = computed<string>({
  get: () => {
    if (mode.value === 'full')
      return FULL;

    return props.simpleOptions.length > 0
      ? simpleKind.value
      : 'simple';
  },
  set: (value: string): void => {
    if (value === FULL) {
      mode.value = 'full';

      return;
    }

    mode.value = 'simple';

    if (props.simpleOptions.length > 0) {
      simpleKind.value = value;
    }
  },
});

// The first action (or block) a person picks makes the pipeline: it is created, given that step and bound.
const starting = ref(false);
const existing = ref(false);
const showPalette = computed<boolean>(() => target.value === null && !existing.value);

async function start(make: (mint: () => string) => PipelineStep[]): Promise<void> {
  starting.value = true;

  try {
    const made = await create.mutateAsync({ name: props.newPipelineName || t('common.replyMode.newName') });

    await save.mutateAsync({ id: made.id, graph: { steps: make(newId) } });
    target.value = { kind: 'pipeline', id: made.id };
  } catch (error: unknown) {
    toast.error(error instanceof Error
      ? errorText(error, t)
      : t('common.replyMode.createFailed'));
  } finally {
    starting.value = false;
  }
}

function startWithAction(type: string): Promise<void> {
  return start((mint) => newAction(type, mint));
}

function startWithBlock(kind: AddableBlock): Promise<void> {
  return start((mint) => newBlock(kind, mint));
}
</script>

<template>
  <div data-reply-mode class="grid gap-3">
    <h3 data-reply-mode-heading class="text-sm font-semibold">{{ $t('common.replyMode.label') }}</h3>
    <SegmentedControl
      v-model="choice"
      :label="$t('common.replyMode.label')"
      :options="options"
      :disabled="disabled"
    />

    <div
      v-if="mode === 'simple'"
      ref="simpleBox"
      data-reply-simple
      class="grid content-start gap-3"
      :style="{ minHeight: `${reservedHeight}px` }"
    >
      <slot />
    </div>

    <Field
      v-else
      data-reply-pipeline
      class="content-start"
      :style="{ minHeight: `${reservedHeight}px` }"
    >
      <div class="flex items-center gap-1.5">
        <FieldLabel>{{ $t('common.replyMode.pipeline') }}</FieldLabel>
        <FieldInfo v-if="target === null">{{ $t('common.replyMode.none') }}</FieldInfo>
      </div>
      <StepPalette
        v-if="showPalette && wordsReady"
        data-reply-palette
        class="h-80"
        :actions="catalog.data.value?.actions ?? []"
        :read-only="locked || starting"
        :target="null"
        @action="startWithAction"
        @block="startWithBlock"
      />
      <TargetPicker
        v-else-if="!showPalette"
        v-model:target="target"
        clearable
        :kinds="kinds"
        :create-name="newPipelineName"
        :disabled="disabled"
        :label="$t('common.replyMode.pipeline')"
      />
      <!-- Actions are the main path; the existing pipeline or command is the quiet way around it. -->
      <Button
        v-if="target === null"
        data-reply-existing
        type="button"
        variant="ghost"
        size="sm"
        class="w-fit"
        :disabled="disabled"
        @click="existing = !existing"
      >
        {{ existing ? $t('common.replyMode.addActions') : $t('common.replyMode.existing') }}
      </Button>
    </Field>
  </div>
</template>
