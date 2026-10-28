<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import type { JsonValue } from '@/lib/json';
// One setting of a step in the inspector, with the control its kind needs. Text and numbers hand their value
// over when the field is left or Enter is pressed, so one typed value is one undo step. An emptied field hands
// over undefined: the step saves without the setting.
import { computed, ref, useId, watch } from 'vue';

import ChevronDown from '~icons/nnz/chevron-down';
import Quote from '~icons/nnz/quote';

import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

import type { OptionContext, PickerKind } from '@/api/pipelines';
import { Field, FieldContent, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';

import ListField from './ListField.vue';
import MapField from './MapField.vue';
import type { FieldRow } from './stepFields';
import StepPicker from './StepPicker.vue';
import { valueText } from './stepWords';

// The text earlier steps hold (stepChain.ts), offered on a template field so a step can reuse those words.
export type EarlierOption = {
  key: string;
  label: string;
  text: string;
};

const props = withDefaults(defineProps<{
  row: FieldRow;
  context?: OptionContext;
  earlier?: EarlierOption[];
  variables?: string[];
}>(), {
  context: undefined,
  earlier: () => [],
  variables: () => [],
});

function useEarlier(option: EarlierOption): void {
  draft.value = option.text;

  if (option.text !== text.value) {
    emit('change', option.text);
  }
}

const emit = defineEmits<{ change: [value: JsonValue | undefined] }>();

const id = useId();
const text = computed(() => valueText(props.row.value) ?? '');

// A templated text field edits a local draft and hands it over when focus leaves the field.
const draft = ref(text.value);

watch(text, (value: string): void => {
  draft.value = value;
});

function commitDraft(event: FocusEvent): void {
  const next = event.relatedTarget;

  const field = event.currentTarget;

  if (next instanceof Node && field instanceof Node && field.contains(next)) return;

  if (draft.value === text.value) return;

  emit('change', draft.value.trim() === ''
    ? undefined
    : draft.value);
}

function typed(event: Event): void {
  const raw = (event.target as HTMLInputElement | HTMLTextAreaElement).value;

  if (raw.trim() === '') {
    emit('change', undefined);
  } else {
    emit('change', props.row.control === 'number'
      ? Number(raw)
      : raw);
  }
}
</script>

<template>
  <Field
    v-if="row.control === 'switch'"
    :data-step-field="row.name"
    orientation="horizontal"
    class="gap-3"
  >
    <FieldContent>
      <div class="flex items-center gap-1.5">
        <FieldLabel :for="id">{{ row.label }}</FieldLabel>
        <FieldInfo v-if="row.help && !row.disabled">{{ row.help }}</FieldInfo>
      </div>
      <p v-if="row.help && row.disabled" class="m-0 text-xs text-muted-foreground">{{ row.help }}</p>
    </FieldContent>
    <Switch
      :id="id"
      :model-value="row.value === true"
      :disabled="row.disabled === true"
      @update:model-value="emit('change', $event)"
    />
  </Field>
  <div
    v-else-if="row.templated && (row.control === 'line' || row.control === 'lines')"
    :data-step-field="row.name"
    @focusout="commitDraft"
  >
    <TemplateField
      v-model="draft"
      context="pipeline"
      :rows="row.control === 'lines' ? 3 : 1"
      :label="row.label"
      :help="row.help"
      :local-names="variables"
    />
    <DropdownMenu v-if="earlier.length > 0">
      <DropdownMenuTrigger as-child>
        <Button
          data-earlier-text
          type="button"
          variant="ghost"
          size="sm"
          class="mt-1"
        >
          <Quote aria-hidden="true" />
          {{ $t('dashboard.pipelines.workbench.inspector.earlier.button') }}
          <ChevronDown aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="start" class="max-w-80">
        <DropdownMenuLabel>{{ $t('dashboard.pipelines.workbench.inspector.earlier.label') }}</DropdownMenuLabel>
        <DropdownMenuItem
          v-for="option in earlier"
          :key="option.key"
          :data-earlier-option="option.key"
          class="flex-col items-start gap-0.5"
          @select="useEarlier(option)"
        >
          <span class="text-xs text-muted-foreground">{{ option.label }}</span>
          <span class="line-clamp-2 wrap-break-word">{{ option.text }}</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  </div>
  <Field v-else :data-step-field="row.name" class="gap-1.5">
    <div class="flex items-center gap-1.5">
      <FieldLabel :for="id">{{ row.label }}</FieldLabel>
      <FieldInfo v-if="row.help || row.templated">
        {{ row.help }}
        <template v-if="row.templated">{{ $t('dashboard.pipelines.workbench.inspector.templated') }}</template>
      </FieldInfo>
    </div>
    <Select
      v-if="row.control === 'choice'"
      :model-value="text"
      @update:model-value="emit('change', String($event))"
    >
      <SelectTrigger :id="id" class="w-full">
        <SelectValue :placeholder="$t('dashboard.pipelines.workbench.inspector.notSet')" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem v-for="choice in row.choices" :key="choice.value" :value="choice.value">
          {{ choice.label }}
        </SelectItem>
      </SelectContent>
    </Select>
    <StepPicker
      v-else-if="row.control === 'pick'"
      :id="id"
      :kind="row.kind as PickerKind"
      :value="text"
      :required="row.required"
      :context="context"
      @change="emit('change', $event)"
    />
    <ListField
      v-else-if="row.control === 'list'"
      :id="id"
      :value="row.value"
      @change="emit('change', $event)"
    />
    <MapField
      v-else-if="row.control === 'map'"
      :id="id"
      :value="row.value"
      @change="emit('change', $event)"
    />
    <Textarea
      v-else-if="row.control === 'lines'"
      :id="id"
      :model-value="text"
      rows="3"
      @change="typed"
    />
    <Input
      v-else-if="row.control === 'line' || row.control === 'number'"
      :id="id"
      :type="row.control === 'number' ? 'number' : 'text'"
      :model-value="text"
      @change="typed"
    />
    <p v-else :id="id" class="m-0 text-sm wrap-break-word" :class="text === '' && 'text-muted-foreground'">
      {{ text || $t('dashboard.pipelines.workbench.inspector.notSet') }}
    </p>
  </Field>
</template>
