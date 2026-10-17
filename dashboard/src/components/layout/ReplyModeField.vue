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
// Only one of the two shows at a time, so an editor never carries both.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePipelines } from '@/api/pipelines';
import type { ReplyMode } from '@/components/layout/replyMode';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const mode = defineModel<ReplyMode>('mode', { required: true });
const pipelineId = defineModel<string>('pipelineId', { default: '' });

// A locked editor shows the setting but lets no one change it.
defineProps<{ disabled?: boolean }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const pipelines = usePipelines(channelId);

const options = computed<{ value: ReplyMode; label: string }[]>(() => [
  { value: 'simple', label: t('common.replyMode.simple') },
  { value: 'full', label: t('common.replyMode.full') },
]);
</script>

<template>
  <div data-reply-mode class="grid gap-3">
    <h3 data-reply-mode-heading class="text-sm font-semibold">{{ $t('common.replyMode.label') }}</h3>
    <SegmentedControl
      v-model="mode"
      :label="$t('common.replyMode.label')"
      :options="options"
      :disabled="disabled"
    />

    <div v-if="mode === 'simple'" data-reply-simple class="grid gap-3">
      <slot />
    </div>

    <Field v-else data-reply-pipeline>
      <div class="flex items-center gap-1.5">
        <FieldLabel>{{ $t('common.replyMode.pipeline') }}</FieldLabel>
        <FieldInfo v-if="pipelineId === ''">{{ $t('common.replyMode.none') }}</FieldInfo>
      </div>
      <!-- One row, and the button always holds its place: picking a pipeline never moves the layout. -->
      <div class="flex items-center gap-2">
        <Select v-model="pipelineId" :disabled="disabled">
          <SelectTrigger :aria-label="$t('common.replyMode.pipeline')" class="min-w-0 flex-1">
            <SelectValue :placeholder="$t('common.replyMode.pipelinePlaceholder')" />
          </SelectTrigger>
          <SelectContent>
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
