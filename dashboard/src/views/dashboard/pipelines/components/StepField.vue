<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One setting of a step in the inspector, with the control its kind needs. Text and numbers hand their value
// over when the field is left or Enter is pressed, so one typed value is one undo step. An emptied field hands
// over undefined: the step saves without the setting.
import { computed, useId } from 'vue';

import type { PickerKind } from '@/api/pipelines';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';

import type { FieldRow } from './stepFields';
import StepPicker from './StepPicker.vue';
import { valueText } from './stepWords';

const props = defineProps<{ row: FieldRow }>();

const emit = defineEmits<{ change: [value: unknown] }>();

const id = useId();
const text = computed(() => valueText(props.row.value) ?? '');

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
    :data-step-field="row.name"
    :orientation="row.control === 'switch' ? 'horizontal' : 'vertical'"
    class="gap-1.5"
  >
    <FieldLabel :for="id">{{ row.label }}</FieldLabel>
    <Switch
      v-if="row.control === 'switch'"
      :id="id"
      :model-value="row.value === true"
      @update:model-value="emit('change', $event)"
    />
    <Select
      v-else-if="row.control === 'choice'"
      :model-value="text"
      @update:model-value="emit('change', $event)"
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
    <FieldDescription v-if="row.help" class="text-xs">{{ row.help }}</FieldDescription>
    <FieldDescription v-if="row.templated" class="text-xs">
      {{ $t('dashboard.pipelines.workbench.inspector.templated') }}
    </FieldDescription>
  </Field>
</template>
