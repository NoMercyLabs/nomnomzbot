<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import type { JsonValue } from '@/lib/json';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';

import type { FieldRow } from './stepFields';
import { readTtsBlocks, setTtsBlockField } from './ttsSegments';

const props = defineProps<{ row: FieldRow }>();
const emit = defineEmits<{ change: [value: JsonValue | undefined] }>();
const { t } = useI18n();
const id = useId();
const page = 'dashboard.pipelines.workbench.ttsBlocks';
const styles = ['cheerful', 'excited', 'sad', 'whispering', 'shouting', 'friendly', 'unfriendly', 'terrified', 'hopeful'];
const readings = ['spell-out', 'cardinal', 'ordinal', 'digits', 'date', 'time', 'telephone'];
const blocks = computed(() => readTtsBlocks(props.row.value));
const raw = ref(typeof props.row.value === 'string'
  ? props.row.value
  : JSON.stringify(props.row.value ?? []));
const rawMode = ref(blocks.value === null);

watch(() => props.row.value, (value) => {
  raw.value = typeof value === 'string'
    ? value
    : JSON.stringify(value ?? []);
});

function edit(index: number, key: string, value: JsonValue | undefined): void {
  if (blocks.value === null) return;

  emit('change', setTtsBlockField(blocks.value, index, key, value));
}

function inputValue(event: Event): string {
  return (event.target as HTMLInputElement).value;
}

function editText(index: number, key: string, event: Event): void {
  const value = inputValue(event);

  edit(index, key, value === ''
    ? undefined
    : value);
}

function editNumber(index: number, key: string, event: Event): void {
  const value = inputValue(event);

  edit(index, key, value === ''
    ? undefined
    : Number(value));
}

function append(block: Record<string, JsonValue>): void {
  if (blocks.value === null) return;

  emit('change', JSON.stringify([...blocks.value, block]));
}

function remove(index: number): void {
  if (blocks.value === null) return;

  const remaining = blocks.value.filter((_, at) => at !== index);

  emit('change', JSON.stringify(remaining));
}

function commitRaw(): void {
  emit('change', raw.value.trim() === ''
    ? undefined
    : raw.value);
}
</script>

<template>
  <Field :data-step-field="row.name" class="gap-3">
    <div class="flex items-center gap-1.5">
      <FieldLabel :for="id">{{ row.label }}</FieldLabel>
      <FieldInfo v-if="row.help">{{ row.help }}</FieldInfo>
    </div>
    <div class="flex justify-end">
      <Button type="button"
              variant="ghost"
              size="sm"
              :disabled="blocks === null"
              @click="rawMode = !rawMode"
      >
        {{ t(rawMode ? `${page}.editBlocks` : `${page}.editJson`) }}
      </Button>
    </div>
    <Textarea
      v-if="rawMode || blocks === null"
      :id="id"
      v-model="raw"
      rows="6"
      @change="commitRaw"
    />
    <div v-else class="space-y-4">
      <div v-for="(block, index) in blocks" :key="index" class="space-y-3 border-t pt-4">
        <div class="flex items-center justify-between">
          <span class="text-sm font-medium">{{ t(`${page}.block`, { number: index + 1 }) }}</span>
          <Button type="button" variant="ghost" size="sm" @click="remove(index)">
            {{ t(`${page}.remove`) }}
          </Button>
        </div>
        <Field v-if="'pause' in block" class="gap-1.5">
          <FieldLabel :for="`${id}-pause-${index}`">{{ t(`${page}.pause`) }}</FieldLabel>
          <Input :id="`${id}-pause-${index}`"
                 type="number"
                 min="0"
                 :model-value="String(block.pause ?? '')"
                 @change="editNumber(index, 'pause', $event)"
          />
        </Field>
        <template v-else>
          <Field class="gap-1.5">
            <FieldLabel :for="`${id}-text-${index}`">{{ t(`${page}.text`) }}</FieldLabel>
            <Textarea :id="`${id}-text-${index}`" :model-value="String(block.text ?? '')" rows="2" @change="editText(index, 'text', $event)" />
          </Field>
          <div class="grid grid-cols-2 gap-3">
            <Field v-for="name in ['voice', 'rate', 'pitch', 'volume', 'breakAfterMs']" :key="name" class="gap-1.5">
              <FieldLabel :for="`${id}-${name}-${index}`">{{ t(`${page}.${name}`) }}</FieldLabel>
              <Input :id="`${id}-${name}-${index}`" :type="name === 'breakAfterMs' ? 'number' : 'text'" :model-value="String(block[name] ?? '')" @change="name === 'breakAfterMs' ? editNumber(index, name, $event) : editText(index, name, $event)" />
            </Field>
          </div>
          <Field class="gap-1.5">
            <div class="flex items-center gap-1.5">
              <FieldLabel :for="`${id}-style-${index}`">{{ t(`${page}.style`) }}</FieldLabel>
              <FieldInfo>{{ t(`${page}.styleHelp`) }}</FieldInfo>
            </div>
            <Select :model-value="String(block.style ?? 'none')" @update:model-value="edit(index, 'style', $event === 'none' ? undefined : String($event))">
              <SelectTrigger :id="`${id}-style-${index}`" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="none">{{ t(`${page}.none`) }}</SelectItem>
                <SelectItem v-for="style in styles" :key="style" :value="style">{{ t(`${page}.styles.${style}`) }}</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field class="gap-1.5">
            <div class="flex items-center gap-1.5">
              <FieldLabel :for="`${id}-sayAs-${index}`">{{ t(`${page}.sayAs`) }}</FieldLabel>
              <FieldInfo>{{ t(`${page}.sayAsHelp`) }}</FieldInfo>
            </div>
            <Select :model-value="String(block.sayAs ?? 'none')" @update:model-value="edit(index, 'sayAs', $event === 'none' ? undefined : String($event))">
              <SelectTrigger :id="`${id}-sayAs-${index}`" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="none">{{ t(`${page}.none`) }}</SelectItem>
                <SelectItem v-for="reading in readings" :key="reading" :value="reading">{{ t(`${page}.readings.${reading}`) }}</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <Field orientation="horizontal" class="gap-3">
            <FieldLabel :for="`${id}-stress-${index}`">{{ t(`${page}.stress`) }}</FieldLabel>
            <Switch :id="`${id}-stress-${index}`" :model-value="block.stress === true" @update:model-value="edit(index, 'stress', $event)" />
          </Field>
        </template>
      </div>
      <div class="flex gap-2">
        <Button type="button" variant="outline" size="sm" @click="append({ text: '' })">{{ t(`${page}.addText`) }}</Button>
        <Button type="button" variant="ghost" size="sm" @click="append({ pause: 500 })">{{ t(`${page}.addPause`) }}</Button>
      </div>
    </div>
  </Field>
</template>
