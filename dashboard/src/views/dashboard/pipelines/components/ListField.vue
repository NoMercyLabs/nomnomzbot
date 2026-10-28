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
// A step setting with many items: one row each, a row is removed on its own, and a new row is empty until it
// is typed in. Empty rows are left out of the saved setting; no rows at all saves without it.
import Add from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';
import { nextTick, ref, useTemplateRef, watch } from 'vue';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';

import { itemText, jsonOf, listItem } from './stepFields';

const props = defineProps<{ id: string; value: JsonValue | undefined }>();

const emit = defineEmits<{ change: [value: JsonValue[] | undefined] }>();

const root = useTemplateRef<HTMLElement>('root');
const items = ref<string[]>([]);
// What this control just handed over: that one echo keeps the rows not filled in yet; undo and redo reset.
let sent: string | null = null;

watch(() =>
  props.value, (value: JsonValue | undefined) => {
  if (jsonOf(value) !== sent) {
    items.value = toItems(value);
  }

  sent = null;
}, { immediate: true });

function toItems(value: JsonValue | undefined): string[] {
  if (Array.isArray(value))
    return value.map(itemText);

  return value === undefined || value === null || value === ''
    ? []
    : [itemText(value)];
}

// An OBS request or other JSON object gets room for its lines.
function isBlock(text: string): boolean {
  return typeof listItem(text) === 'object' || text.includes('\n');
}

function save(): void {
  const kept = items.value
    .filter((text) =>
      text.trim() !== '')
    .map(listItem);
  const value = kept.length === 0
    ? undefined
    : kept;

  sent = jsonOf(value);
  emit('change', value);
}

function typed(index: number, event: Event): void {
  items.value[index] = (event.target as HTMLInputElement | HTMLTextAreaElement).value;
  save();
}

function remove(index: number): void {
  items.value.splice(index, 1);
  save();
}

async function add(): Promise<void> {
  items.value.push('');
  await nextTick();

  const fields = root.value?.querySelectorAll<HTMLElement>('input, textarea');

  fields?.item(fields.length - 1).focus();
}
</script>

<template>
  <div :id="id" ref="root" data-list-field class="flex flex-col gap-2">
    <div v-for="(item, index) in items" :key="index" data-list-item class="flex items-start gap-2">
      <Textarea
        v-if="isBlock(item)"
        :model-value="item"
        :aria-label="$t('dashboard.pipelines.workbench.list.item', { number: index + 1 })"
        rows="4"
        class="font-mono text-xs"
        @change="typed(index, $event)"
      />
      <Input
        v-else
        :model-value="item"
        :aria-label="$t('dashboard.pipelines.workbench.list.item', { number: index + 1 })"
        @change="typed(index, $event)"
      />
      <Button
        type="button"
        variant="ghost"
        size="icon"
        data-list-remove
        :aria-label="$t('dashboard.pipelines.workbench.list.remove', { number: index + 1 })"
        @click="remove(index)"
      >
        <Cross aria-hidden="true" />
      </Button>
    </div>
    <Button type="button"
            variant="outline"
            size="sm"
            data-list-add
            class="self-start"
            @click="add"
    >
      <Add aria-hidden="true" />
      {{ $t('dashboard.pipelines.workbench.list.add') }}
    </Button>
  </div>
</template>
