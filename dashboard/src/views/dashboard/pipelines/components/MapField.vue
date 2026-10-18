<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import type { JsonValue } from '@/lib/json';
// A step setting of names with values, one row each. A row without a name is left out; a name used twice is
// shown as a mistake and not saved, because only one of them could count.
import Add from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue';

import { Button } from '@/components/ui/button';
import { FieldError } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

import { itemText, jsonOf } from './stepFields';

type Pair = { name: string; value: string };

const props = defineProps<{ id: string; value: JsonValue | undefined }>();

const emit = defineEmits<{ change: [value: Record<string, string> | undefined] }>();

const root = useTemplateRef<HTMLElement>('root');
const pairs = ref<Pair[]>([]);
// What this control just handed over: that one echo keeps the rows not filled in yet; undo and redo reset.
let sent: string | null = null;

watch(() =>
  props.value, (value: JsonValue | undefined) => {
  if (jsonOf(value) !== sent) {
    pairs.value = toPairs(value);
  }

  sent = null;
}, { immediate: true });

function toPairs(value: JsonValue | undefined): Pair[] {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    return [];

  return Object.entries(value).map(([name, saved]) =>
    ({ name, value: itemText(saved) }));
}

const named = computed(() =>
  pairs.value.filter((pair) =>
    pair.name.trim() !== ''));
const twice = computed(() =>
  named.value
    .map((pair) =>
      pair.name.trim())
    .find((name, index, names) =>
      names.indexOf(name) !== index));

function save(): void {
  if (twice.value !== undefined) return;

  const value = named.value.length === 0
    ? undefined
    : Object.fromEntries(named.value.map((pair) =>
        [pair.name.trim(), pair.value]));

  sent = jsonOf(value);
  emit('change', value);
}

function typed(index: number, part: keyof Pair, event: Event): void {
  pairs.value[index]![part] = (event.target as HTMLInputElement).value;
  save();
}

function remove(index: number): void {
  pairs.value.splice(index, 1);
  save();
}

async function add(): Promise<void> {
  pairs.value.push({ name: '', value: '' });
  await nextTick();

  const names = root.value?.querySelectorAll<HTMLElement>('[data-map-name]');

  names?.item(names.length - 1).focus();
}
</script>

<template>
  <div :id="id" ref="root" data-map-field class="flex flex-col gap-2">
    <div v-for="(pair, index) in pairs" :key="index" data-map-row class="flex items-center gap-2">
      <Input
        :model-value="pair.name"
        data-map-name
        :aria-label="$t('dashboard.pipelines.workbench.map.name', { number: index + 1 })"
        :placeholder="$t('dashboard.pipelines.workbench.map.namePlaceholder')"
        class="min-w-0 flex-1"
        @change="typed(index, 'name', $event)"
      />
      <Input
        :model-value="pair.value"
        data-map-value
        :aria-label="$t('dashboard.pipelines.workbench.map.value', { number: index + 1 })"
        :placeholder="$t('dashboard.pipelines.workbench.map.valuePlaceholder')"
        class="min-w-0 flex-1"
        @change="typed(index, 'value', $event)"
      />
      <Button
        type="button"
        variant="ghost"
        size="icon"
        data-map-remove
        :aria-label="$t('dashboard.pipelines.workbench.map.remove', { number: index + 1 })"
        @click="remove(index)"
      >
        <Cross aria-hidden="true" />
      </Button>
    </div>
    <FieldError v-if="twice !== undefined" data-map-twice>
      {{ $t('dashboard.pipelines.workbench.map.twice', { name: twice }) }}
    </FieldError>
    <Button type="button"
            variant="outline"
            size="sm"
            data-map-add
            class="self-start"
            @click="add"
    >
      <Add aria-hidden="true" />
      {{ $t('dashboard.pipelines.workbench.map.add') }}
    </Button>
  </div>
</template>
