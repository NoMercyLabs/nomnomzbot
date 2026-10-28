<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The fields of a feed: a name the pipelines read as {{custom.<feed>.<name>}} and the path of its value in the
// data the source sends. The rows are a list so the order the streamer typed stays.
import Add from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export type FieldRow = { name: string; path: string };

const rows = defineModel<FieldRow[]>({ required: true });

function add(): void {
  rows.value = [...rows.value, { name: '', path: '' }];
}

function remove(index: number): void {
  rows.value = rows.value.filter((_row, position) => position !== index);
}

function edit(index: number, part: keyof FieldRow, value: string | number): void {
  rows.value = rows.value.map((row, position) =>
    position === index
      ? { ...row, [part]: String(value) }
      : row);
}
</script>

<template>
  <div data-field-map class="grid gap-2">
    <div
      v-for="(row, index) in rows"
      :key="index"
      data-field-row
      class="flex items-center gap-2"
    >
      <Input
        :model-value="row.name"
        data-field-name
        class="w-40"
        maxlength="50"
        :aria-label="$t('dashboard.settings.customEventFeeds.fields.name')"
        :placeholder="$t('dashboard.settings.customEventFeeds.fields.namePlaceholder')"
        @update:model-value="edit(index, 'name', $event)"
      />
      <Input
        :model-value="row.path"
        data-field-path
        class="min-w-0 flex-1 font-mono"
        :aria-label="$t('dashboard.settings.customEventFeeds.fields.path')"
        :placeholder="$t('dashboard.settings.customEventFeeds.fields.pathPlaceholder')"
        @update:model-value="edit(index, 'path', $event)"
      />
      <Button
        type="button"
        variant="ghost"
        size="icon"
        data-field-remove
        :aria-label="$t('dashboard.settings.customEventFeeds.fields.remove')"
        @click="remove(index)"
      >
        <Cross class="size-5" />
      </Button>
    </div>
    <Button type="button"
            variant="outline"
            size="sm"
            class="w-fit"
            data-field-add
            @click="add"
    >
      <Add class="size-4" />
      {{ $t('dashboard.settings.customEventFeeds.fields.add') }}
    </Button>
  </div>
</template>
