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
import Plus from '~icons/nnz/add';
import { computed, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreatePipeline, usePipelines, type Pipeline } from '@/api/pipelines';
import type { ReplyMode } from '@/components/layout/replyMode';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

type Option = { value: string; label: string };

const mode = defineModel<ReplyMode>('mode', { required: true });
const pipelineId = defineModel<string>('pipelineId', { default: '' });
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

const CREATE = '__create';
const FULL = 'full';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const pipelines = usePipelines(channelId);
const create = useCreatePipeline(channelId);

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

const picked = computed<string>({
  get: () => pipelineId.value,
  set: (value: string): void => {
    if (value === CREATE) {
      void createPipeline();

      return;
    }

    pipelineId.value = value;
  },
});

async function createPipeline(): Promise<void> {
  const name: string = props.newPipelineName || t('common.replyMode.newName');
  const made: Pipeline = await create.mutateAsync({ name });

  pipelineId.value = made.id;
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
        <FieldInfo v-if="pipelineId === ''">{{ $t('common.replyMode.none') }}</FieldInfo>
      </div>
      <!-- One row, and the button always holds its place: picking a pipeline never moves the layout. -->
      <div class="flex items-center gap-2">
        <Select v-model="picked" :disabled="disabled || create.isPending.value">
          <SelectTrigger :aria-label="$t('common.replyMode.pipeline')" class="min-w-0 flex-1">
            <SelectValue :placeholder="$t('common.replyMode.pipelinePlaceholder')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem data-pipeline-create :value="CREATE">
              <Plus aria-hidden="true" />
              {{ $t('common.replyMode.create') }}
            </SelectItem>
            <SelectSeparator />
            <SelectItem
              v-for="pipeline in pipelines.data.value ?? []"
              :key="pipeline.id"
              :value="pipeline.id"
            >
              {{ pipeline.name }}
            </SelectItem>
          </SelectContent>
        </Select>
        <Button
          v-if="pipelineId !== ''"
          as-child
          type="button"
          variant="outline"
          class="flex-none"
        >
          <RouterLink :to="`/app/pipelines/${pipelineId}`">
            {{ $t('common.replyMode.open') }}
          </RouterLink>
        </Button>
        <Button
          v-else
          type="button"
          variant="outline"
          class="flex-none"
          disabled
        >
          {{ $t('common.replyMode.open') }}
        </Button>
      </div>
    </Field>
  </div>
</template>
