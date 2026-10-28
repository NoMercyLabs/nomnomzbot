<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Picks one operator or one channel by name for an audit filter. The filter carries the id of the pick;
// nobody types an id. The parent runs the search and hands in what it found.
import Cross from '~icons/nnz/cross';
import { computed } from 'vue';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export type AuditPickerOption = {
  id: string;
  name: string;
  hint: string;
};

const props = defineProps<{
  picked: string;
  options: readonly AuditPickerOption[];
  placeholder: string;
  clearLabel: string;
  noneText: string;
  settled: boolean;
}>();

const emit = defineEmits<{ pick: [option: AuditPickerOption | null] }>();

const search = defineModel<string>('search', { required: true });

const asking = computed<boolean>(() => search.value.trim() !== '');

function choose(option: AuditPickerOption): void {
  search.value = '';
  emit('pick', option);
}
</script>

<template>
  <div>
    <div
      v-if="props.picked !== ''"
      data-picker-picked
      class="flex min-h-9 items-center justify-between gap-2 rounded-md border border-input px-3 text-sm"
    >
      <span class="truncate">{{ props.picked }}</span>
      <Button
        variant="ghost"
        size="icon"
        data-picker-clear
        :aria-label="props.clearLabel"
        @click="emit('pick', null)"
      >
        <Cross class="size-5" />
      </Button>
    </div>
    <template v-else>
      <Input v-model="search" data-picker-search autocomplete="off" :placeholder="props.placeholder" />
      <ul
        v-if="asking && props.options.length > 0"
        class="mt-1.5 max-h-56 w-full overflow-auto rounded-md border bg-popover p-1 text-popover-foreground"
      >
        <li v-for="option in props.options" :key="option.id">
          <button
            type="button"
            data-picker-option
            class="flex min-h-11 w-full flex-col justify-center rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent hover:text-accent-foreground"
            @click="choose(option)"
          >
            <span>{{ option.name }}</span>
            <span class="text-xs text-muted-foreground">{{ option.hint }}</span>
          </button>
        </li>
      </ul>
      <p v-else-if="asking && props.settled" class="mt-1.5 text-sm text-muted-foreground">
        {{ props.noneText }}
      </p>
    </template>
  </div>
</template>
