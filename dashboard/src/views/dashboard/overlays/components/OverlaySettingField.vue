<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import type { JsonObject, JsonValue } from '@/lib/json';
// One settings field: its label and help on the left, the control its type asks for,
// and Reset when the value differs from the default (12-overlays.html:304-313).
// A JSON setting gets a control per key; a value no control can hold says it is set in the code.
import RotateCcw from '~icons/nnz/arrow-counter-clockwise';
import { computed, useId } from 'vue';

import type { PickerKind } from '@/api/pipelines';
import type { WidgetSettingsField } from '@/api/widgets';
import MultiChoice from '@/components/layout/MultiChoice.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useLocalizedText } from '@/composables/useLocalizedText';
import { humanize } from '@/lib/text';
import StepPicker from '@/views/dashboard/pipelines/components/StepPicker.vue';

import SettingGroup from './SettingGroup.vue';
import {
  clampNumber,
  objectOf,
  parsedJson,
  pickedWords,
  pickerOf,
  textListOf,
  withoutCodeEditorNote,
} from './settingField';
import StringListEditor from './StringListEditor.vue';

const props = defineProps<{
  field: WidgetSettingsField;
  value: JsonValue | undefined;
  changed: boolean;
}>();
const emit = defineEmits<{ update: [value: JsonValue | undefined]; reset: [] }>();

const text = useLocalizedText();
const titleId = useId();

// A picked field drops the "ID" from its words; a JSON field edited here drops the "edited in the code editor" note.
function plain(words: string): string {
  const picked = pickerKind.value
    ? pickedWords(words)
    : words;

  return group.value || textList.value
    ? withoutCodeEditorNote(picked)
    : picked;
}

const declaredLabel = computed<string>(() => text(props.field.label, humanize(props.field.key)));
const label = computed<string>(() => plain(declaredLabel.value));
const help = computed<string>(() =>
  props.field.help
    ? plain(text(props.field.help, ''))
    : '');

interface Choice {
  value: string;
  label: string;
}
const choices = computed<Choice[]>(() =>
  (props.field.options ?? []).map((option) => ({
    value: option.value,
    label: text(option.label, humanize(option.value)),
  }) as const satisfies Choice),
);
const chosenLabel = computed<string>(
  () =>
    choices.value.find((choice) => choice.value === props.value)?.label ??
    String(props.value ?? ''),
);
const picked = computed<string[]>(() =>
  Array.isArray(props.value)
    ? props.value.map(String)
    : [],
);
const shown = computed<string>(() =>
  props.value === null || props.value === undefined
    ? ''
    : String(props.value),
);
// A text field that names a kind of thing the bot can list (a reward, an OBS input) is picked from that list.
const pickerKind = computed<PickerKind | null>(() => pickerOf(props.field));
// A group, or a JSON setting that holds an object, is edited key by key; a JSON list of texts row by row.
const structured = computed(() => parsedJson(props.value));
const group = computed<JsonObject | null>(() => {
  if (props.field.type === 'group')
    return objectOf(props.value) ?? {};

  return props.field.type === 'json'
    ? objectOf(structured.value)
    : null;
});
const textList = computed<string[] | null>(() => {
  if (props.field.type === 'list')
    return picked.value;

  return props.field.type === 'json'
    ? textListOf(structured.value)
    : null;
});
const wide = computed<boolean>(() =>
  ['multiselect', 'list', 'group', 'json'].includes(props.field.type));
const controlWidth = computed<string>(() => {
  if (props.field.type === 'bool')
    return '';

  return wide.value
    ? 'w-full'
    : 'w-full sm:w-80';
});
const known = computed<boolean>(() =>
  ['bool', 'number', 'text', 'color', 'select', 'multiselect', 'list', 'group', 'json'].includes(
    props.field.type,
  ),
);

// The number is held inside min and max on change; the box shows the held value even when it did not move.
function onNumber(event: Event): void {
  const box = event.target as HTMLInputElement;
  const next = clampNumber(box.value, props.field, props.value);

  box.value = String(next);
  emit('update', next);
}
</script>

<template>
  <div
    :data-setting="field.key"
    :data-changed="changed"
    class="flex flex-wrap items-center gap-x-3 gap-y-2"
    :class="field.type === 'bool' ? 'pr-3' : 'px-3 py-2'"
  >
    <SettingRow
      v-if="field.type === 'bool'"
      class="min-w-0 flex-1"
      :model-value="value === true"
      :title="label"
      :description="help || undefined"
      @update:model-value="emit('update', $event)"
    />
    <template v-else>
      <span class="min-w-40 flex-1">
        <span :id="titleId" class="block text-sm font-medium wrap-break-word">{{ label }}</span>
        <span v-if="help" class="block text-sm text-muted-foreground wrap-break-word">{{
          help
        }}</span>
      </span>
    </template>

    <!-- The control and its Reset wrap as one, so Reset never lands alone on a line. -->
    <span
      class="flex min-w-0 items-center gap-1"
      :class="controlWidth"
    >
      <Input
        v-if="field.type === 'number'"
        type="number"
        class="min-w-0 flex-1"
        :min="field.min ?? undefined"
        :max="field.max ?? undefined"
        :step="field.step ?? undefined"
        :model-value="shown"
        :aria-labelledby="titleId"
        @change="onNumber"
      />
      <span v-else-if="field.type === 'color'" class="relative min-w-0 flex-1">
        <span
          data-swatch
          class="absolute top-1/2 left-2 size-5 -translate-y-1/2 rounded-xs border border-border"
          :style="{ backgroundColor: shown }"
          aria-hidden="true"
        />
        <Input
          class="pl-9 font-mono"
          :model-value="shown"
          :aria-labelledby="titleId"
          @update:model-value="emit('update', String($event))"
        />
      </span>
      <StepPicker
        v-else-if="field.type === 'text' && pickerKind"
        :id="titleId + '-picker'"
        class="min-w-0 flex-1"
        :kind="pickerKind"
        :value="shown"
        :required="false"
        @change="emit('update', $event ?? '')"
      />
      <Input
        v-else-if="field.type === 'text'"
        class="min-w-0 flex-1"
        :model-value="shown"
        :aria-labelledby="titleId"
        @update:model-value="emit('update', String($event))"
      />
      <Select
        v-else-if="field.type === 'select'"
        :model-value="shown"
        @update:model-value="emit('update', String($event))"
      >
        <SelectTrigger class="min-w-0 flex-1" :aria-labelledby="titleId">
          <SelectValue>{{ chosenLabel }}</SelectValue>
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="choice in choices" :key="choice.value" :value="choice.value">
            {{ choice.label }}
          </SelectItem>
        </SelectContent>
      </Select>
      <MultiChoice
        v-else-if="field.type === 'multiselect'"
        class="min-w-0 flex-1"
        :options="choices"
        :model-value="picked"
        :aria-labelledby="titleId"
        @update:model-value="emit('update', $event)"
      />
      <StringListEditor
        v-else-if="textList"
        class="min-w-0 flex-1"
        :items="textList"
        @update="emit('update', $event)"
      />
      <SettingGroup
        v-else-if="group"
        class="min-w-0 flex-1"
        :value="group"
        @update="emit('update', $event)"
      />
      <p
        v-else-if="field.type !== 'bool'"
        data-in-code
        class="min-w-0 flex-1 text-sm text-muted-foreground"
      >
        {{ $t('dashboard.overlays.detail.settings.inCode') }}
      </p>

      <Button
        v-if="changed && known"
        data-reset
        class="flex-none"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.overlays.detail.settings.reset', { name: label })"
        :title="$t('dashboard.overlays.detail.settings.reset', { name: label })"
        @click="emit('reset')"
      >
        <RotateCcw aria-hidden="true" />
      </Button>
      <span v-else class="size-9 flex-none" aria-hidden="true" />
    </span>
  </div>
</template>
