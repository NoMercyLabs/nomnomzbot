<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What a test run starts with: names with values the steps read as {name}, as a real start would fill them.
import { computed, ref } from 'vue';

import { Button } from '@/components/ui/button';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import type { PipelineInput } from '@/api/pipelines';
import type { JsonValue } from '@/lib/json';

import ChevronDown from '~icons/nnz/chevron-down';

import MapField from './MapField.vue';

const input = defineModel<Record<string, string>>({ required: true });
const values = defineModel<Record<string, JsonValue>>('inputs', { required: true });
const props = defineProps<{ definitions: PipelineInput[] }>();
const emit = defineEmits<{ confirm: [] }>();
const open = ref(false);

function show(): void {
  open.value = true;
}

defineExpose({ show });

const count = computed(() => Object.keys(input.value).length + Object.keys(values.value).length);
const rawDrafts = ref<Record<string, string>>({});
const invalid = ref<Record<string, boolean>>({});
const cannotRun = computed(() => props.definitions.some((definition) =>
  (definition.required && values.value[definition.name] === undefined) ||
  invalid.value[definition.name]));

function submit(): void {
  if (cannotRun.value) return;

  open.value = false;
  emit('confirm');
}

function change(value: Record<string, string> | undefined): void {
  input.value = value ?? {};
}

function shown(definition: PipelineInput): string {
  const draft = rawDrafts.value[definition.name];

  if (draft !== undefined)
    return draft;

  const value = values.value[definition.name];

  if (value === undefined)
    return '';

  return definition.type === 'text'
    ? String(value)
    : JSON.stringify(value);
}

function setValue(definition: PipelineInput, event: Event): void {
  const raw = (event.target as HTMLInputElement).value;

  rawDrafts.value[definition.name] = raw;

  if (raw.trim() === '') {
    const next = { ...values.value };

    delete next[definition.name];
    values.value = next;
    invalid.value[definition.name] = false;

    return;
  }

  if (definition.type === 'text') {
    values.value = { ...values.value, [definition.name]: raw };
    invalid.value[definition.name] = false;

    return;
  }

  try {
    const parsed: JsonValue = JSON.parse(raw);
    let correct = false;

    if (definition.type === 'number') {
      correct = typeof parsed === 'number';
    }

    if (definition.type === 'boolean') {
      correct = typeof parsed === 'boolean';
    }

    if (definition.type === 'list') {
      correct = Array.isArray(parsed);
    }

    if (definition.type === 'object') {
      correct = typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed);
    }

    invalid.value[definition.name] = !correct;

    if (correct) {
      values.value = { ...values.value, [definition.name]: parsed as JsonValue };
    }
  } catch {
    invalid.value[definition.name] = true;
  }
}
</script>

<template>
  <Button data-run-input
          type="button"
          variant="ghost"
          size="sm"
          @click="open = true"
  >
    {{ $t('dashboard.pipelines.workbench.run.input', { count }, count) }}
    <ChevronDown aria-hidden="true" />
  </Button>
  <FormDialog
    v-model:open="open"
    data-run-input-dialog
    :title="$t('dashboard.pipelines.workbench.run.inputDialog.title')"
    :description="$t('dashboard.pipelines.workbench.run.inputDialog.description')"
    :action="$t('dashboard.pipelines.workbench.run.inputDialog.action')"
    :disabled="cannotRun"
    @submit="submit"
  >
    <div class="flex flex-col gap-3">
      <Field v-for="definition in definitions" :key="definition.name">
        <FieldLabel :for="`run-${definition.name}`">
          {{ definition.name }}
        </FieldLabel>
        <Input :id="`run-${definition.name}`"
               :data-run-input-name="definition.name"
               :model-value="shown(definition)"
               :aria-invalid="invalid[definition.name] || undefined"
               @change="setValue(definition, $event)"
        />
        <FieldInfo>
          {{ definition.description || $t('dashboard.pipelines.workbench.inputs.runHelp') }}
          <template v-if="definition.required">{{ $t('dashboard.pipelines.workbench.inputs.required') }}</template>
        </FieldInfo>
      </Field>
      <p class="m-0 text-sm text-muted-foreground">
        {{ $t('dashboard.pipelines.workbench.run.inputHelp') }}
      </p>
      <MapField id="test-run-input" :value="input" @change="change" />
    </div>
  </FormDialog>
</template>
