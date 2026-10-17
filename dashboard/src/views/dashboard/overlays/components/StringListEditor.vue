<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
