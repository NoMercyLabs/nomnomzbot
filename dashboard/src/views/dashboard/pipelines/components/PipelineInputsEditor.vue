<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import Add from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';
import { ref } from 'vue';

import type { PipelineInput } from '@/api/pipelines';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import type { JsonValue } from '@/lib/json';

const inputs = defineModel<PipelineInput[]>({ required: true });

defineProps<{ readOnly: boolean }>();

const invalidDefaults = ref<Record<number, boolean>>({});
const types = ['text', 'number', 'boolean', 'list', 'object'] as const;

function change(index: number, patch: Partial<PipelineInput>): void {
  inputs.value = inputs.value.map((input, place) =>
    place === index
      ? { ...input, ...patch }
      : input);
}

function add(): void {
  inputs.value = [...inputs.value, {
    name: '',
    type: 'text',
    required: true,
    defaultValue: null,
    description: '',
  }];
}

function remove(index: number): void {
  inputs.value = inputs.value.filter((_, place) => place !== index);
}

function text(event: Event): string {
  return (event.target as HTMLInputElement).value;
}

function setDefault(index: number, raw: string): void {
  const type = inputs.value[index]?.type;

  if (!type) return;

  if (raw.trim() === '') {
    invalidDefaults.value[index] = false;
    change(index, { defaultValue: null });

    return;
  }

  if (type === 'text') {
    invalidDefaults.value[index] = false;
    change(index, { defaultValue: raw });

    return;
  }

  try {
    const value: JsonValue = JSON.parse(raw);
    const matches = matchesType(type, value);

    invalidDefaults.value[index] = !matches;

    if (matches) {
      change(index, { defaultValue: value as PipelineInput['defaultValue'] });
    }
  } catch {
    invalidDefaults.value[index] = true;
  }
}

function matchesType(type: PipelineInput['type'], value: JsonValue): boolean {
  if (type === 'number')
    return typeof value === 'number';

  if (type === 'boolean')
    return typeof value === 'boolean';

  if (type === 'list')
    return Array.isArray(value);

  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function defaultText(input: PipelineInput): string {
  if (input.defaultValue === null)
    return '';

  return input.type === 'text'
    ? String(input.defaultValue)
    : JSON.stringify(input.defaultValue);
}
</script>

<template>
  <section data-pipeline-inputs class="flex flex-col gap-3 rounded-lg border bg-card p-4">
    <div class="flex items-center justify-between gap-3">
      <div>
        <h2 class="text-sm font-semibold">{{ $t('dashboard.pipelines.workbench.inputs.title') }}</h2>
        <p class="text-sm text-muted-foreground">{{ $t('dashboard.pipelines.workbench.inputs.help') }}</p>
      </div>
      <Button v-if="!readOnly"
              data-input-add
              type="button"
              variant="outline"
              size="sm"
              @click="add"
      >
        <Add aria-hidden="true" />
        {{ $t('dashboard.pipelines.workbench.inputs.add') }}
      </Button>
    </div>
    <p v-if="inputs.length === 0" class="text-sm text-muted-foreground">
      {{ $t('dashboard.pipelines.workbench.inputs.empty') }}
    </p>
    <div v-for="(input, index) in inputs" :key="index" data-input-row class="grid gap-3 rounded-md border p-3 sm:grid-cols-2">
      <Field>
        <FieldLabel :for="`input-name-${index}`">{{ $t('dashboard.pipelines.workbench.inputs.name') }}</FieldLabel>
        <Input :id="`input-name-${index}`"
               data-input-name
               :model-value="input.name"
               :disabled="readOnly"
               @change="change(index, { name: text($event) })"
        />
      </Field>
      <Field>
        <FieldLabel :for="`input-type-${index}`">{{ $t('dashboard.pipelines.workbench.inputs.type') }}</FieldLabel>
        <Select :model-value="input.type"
                :disabled="readOnly"
                @update:model-value="change(index, { type: $event as PipelineInput['type'], defaultValue: null })"
        >
          <SelectTrigger :id="`input-type-${index}`" data-input-type><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="type in types" :key="type" :value="type">
              {{ $t(`dashboard.pipelines.workbench.inputs.types.${type}`) }}
            </SelectItem>
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel :for="`input-description-${index}`">{{ $t('dashboard.pipelines.workbench.inputs.description') }}</FieldLabel>
        <Input :id="`input-description-${index}`"
               data-input-description
               :model-value="input.description"
               :disabled="readOnly"
               @change="change(index, { description: text($event) })"
        />
      </Field>
      <Field>
        <FieldLabel :for="`input-default-${index}`">{{ $t('dashboard.pipelines.workbench.inputs.default') }}</FieldLabel>
        <Input :id="`input-default-${index}`"
               data-input-default
               :model-value="defaultText(input)"
               :disabled="readOnly || input.required"
               :aria-invalid="invalidDefaults[index] || undefined"
               @change="setDefault(index, text($event))"
        />
        <FieldInfo>{{ $t('dashboard.pipelines.workbench.inputs.defaultHelp') }}</FieldInfo>
      </Field>
      <div class="flex items-center gap-3 sm:col-span-2">
        <Switch :id="`input-required-${index}`"
                data-input-required
                :model-value="input.required"
                :disabled="readOnly"
                @update:model-value="change(index, { required: $event, defaultValue: null })"
        />
        <FieldLabel :for="`input-required-${index}`">{{ $t('dashboard.pipelines.workbench.inputs.required') }}</FieldLabel>
        <Button v-if="!readOnly"
                data-input-remove
                type="button"
                variant="ghost"
                size="sm"
                class="ml-auto"
                @click="remove(index)"
        >
          <Cross aria-hidden="true" />
          {{ $t('dashboard.pipelines.workbench.inputs.remove') }}
        </Button>
      </div>
    </div>
  </section>
</template>
