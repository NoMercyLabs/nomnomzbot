<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A list of texts edited row by row: type in a row, move it up or down, remove it, or add a row at the end.
import Add from '~icons/nnz/add';
import ArrowDown from '~icons/nnz/arrow-down';
import ArrowUp from '~icons/nnz/arrow-up';
import Trash from '~icons/nnz/trash';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { moveItem } from '@/lib/array';

const props = defineProps<{ items: string[] }>();
const emit = defineEmits<{ update: [items: string[]] }>();

function edit(index: number, value: string): void {
  emit('update', props.items.map((item, at) =>
    at === index
      ? value
      : item));
}

function remove(index: number): void {
  emit('update', props.items.filter((_, at) => at !== index));
}

function move(index: number, to: number): void {
  emit('update', moveItem(props.items, index, to));
}

function add(): void {
  emit('update', [...props.items, '']);
}
</script>

<template>
  <div class="flex w-full flex-col gap-2">
    <p v-if="items.length === 0" class="text-sm text-muted-foreground">
      {{ $t('dashboard.overlays.detail.settings.list.empty') }}
    </p>

    <div v-for="(item, index) in items" :key="index" data-list-item class="flex items-center gap-1">
      <Input
        class="min-w-0 flex-1"
        :model-value="item"
        :aria-label="$t('dashboard.overlays.detail.settings.list.item', { number: index + 1 })"
        @update:model-value="edit(index, String($event))"
      />
      <Button
        data-list-up
        variant="ghost"
        size="icon"
        :disabled="index === 0"
        :aria-label="$t('dashboard.overlays.detail.settings.list.up', { number: index + 1 })"
        @click="move(index, index - 1)"
      >
        <ArrowUp class="size-5" aria-hidden="true" />
      </Button>
      <Button
        data-list-down
        variant="ghost"
        size="icon"
        :disabled="index === items.length - 1"
        :aria-label="$t('dashboard.overlays.detail.settings.list.down', { number: index + 1 })"
        @click="move(index, index + 1)"
      >
        <ArrowDown class="size-5" aria-hidden="true" />
      </Button>
      <Button
        data-list-remove
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.overlays.detail.settings.list.remove', { number: index + 1 })"
        @click="remove(index)"
      >
        <Trash class="size-5" aria-hidden="true" />
      </Button>
    </div>

    <Button data-list-add variant="outline" class="self-start" @click="add">
      <Add class="size-5" aria-hidden="true" />
      {{ $t('dashboard.overlays.detail.settings.list.add') }}
    </Button>
  </div>
</template>
