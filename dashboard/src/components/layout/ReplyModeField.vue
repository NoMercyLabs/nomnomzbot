<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The reply section of an editor: one switch between a simple reply and a full pipeline. The simple reply
// is the default slot. The full pipeline is a picker, because the pipeline has its own reply step.
// Only one of the two shows at a time, so an editor never carries both. An editor with more than one kind
// of simple reply (text, random) passes them as simpleOptions; they sit in the same one switch as the pipeline.
// The picker chooses a pipeline or creates a new empty one in place.
import { useElementSize } from '@vueuse/core';
import { computed, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ReplyMode } from '@/components/layout/replyMode';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import TargetPicker from '@/components/layout/TargetPicker.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import type { PickedTarget, TargetKind } from '@/lib/bindingTarget';

type Option = { value: string; label: string };

const mode = defineModel<ReplyMode>('mode', { required: true });
const pipelineId = defineModel<string>('pipelineId', { default: '' });
const target = defineModel<PickedTarget | null>('target', { default: null });
const simpleKind = defineModel<string>('simpleKind', { default: '' });

// A locked editor shows the setting but lets no one change it.
const props = withDefaults(
  defineProps<{
    disabled?: boolean;
    simpleOptions?: readonly Option[];
    newPipelineName?: string;
    kinds?: readonly TargetKind[];
  }>(),
  {
    disabled: false,
    simpleOptions: () => [],
    newPipelineName: '',
    kinds: () => ['pipeline'],
  },
);

const FULL = 'full';

const { t } = useI18n();

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

// A surface that binds commands too passes `target`; the others keep binding a pipeline id.
const bindsTarget = computed<boolean>(() => props.kinds.includes('command'));
const picker = computed<PickedTarget | null>({
  get: () => {
    if (bindsTarget.value)
      return target.value;

    return pipelineId.value === ''
      ? null
      : { kind: 'pipeline' as const, id: pipelineId.value };
  },
  set: (value: PickedTarget | null): void => {
    if (bindsTarget.value) {
      target.value = value;

      return;
    }

    pipelineId.value = value?.kind === 'pipeline'
      ? value.id
      : '';
  },
});
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
        <FieldInfo v-if="picker === null">{{ $t('common.replyMode.none') }}</FieldInfo>
      </div>
      <TargetPicker
        v-model:target="picker"
        :kinds="kinds"
        :create-name="newPipelineName"
        :disabled="disabled"
        :label="$t('common.replyMode.pipeline')"
      />
    </Field>
  </div>
</template>
