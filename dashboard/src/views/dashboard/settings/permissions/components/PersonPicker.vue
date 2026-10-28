<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Picks one person the bot knows by typing a name. The bot answers with real people; there is no free id field.
import Cross from '~icons/nnz/cross';
import { computed, ref } from 'vue';

import { useViewerSearch, type ViewerOption } from '@/api/community';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Popover, PopoverAnchor, PopoverContent } from '@/components/ui/popover';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

defineProps<{ picked: ViewerOption | null; label: string }>();

const emit = defineEmits<{ pick: [person: ViewerOption | null] }>();

const { channelId } = useCurrentChannel();
const search = ref('');
const found = useViewerSearch(channelId, search);
const listOpen = computed<boolean>(
  () => search.value.trim() !== '' && (found.data.value?.length ?? 0) > 0,
);

function choose(person: ViewerOption): void {
  search.value = '';
  emit('pick', person);
}
</script>

<template>
  <div class="relative w-64">
    <div v-if="picked" data-person-picked class="flex min-h-9 items-center justify-between gap-2 rounded-md border px-3 text-sm">
      <span class="truncate">{{ picked.label }}</span>
      <Button variant="ghost" size="icon" :aria-label="$t('dashboard.settings.permissions.person.clear')" @click="emit('pick', null)">
        <Cross class="size-5" />
      </Button>
    </div>
    <!-- The list opens in a portal: the card around this picker clips anything drawn inside it. -->
    <Popover v-else :open="listOpen">
      <PopoverAnchor as-child>
        <Input v-model="search" data-person-search :aria-label="label" :placeholder="$t('dashboard.settings.permissions.person.placeholder')" />
      </PopoverAnchor>
      <PopoverContent
        align="start"
        class="max-h-56 w-64 gap-0 overflow-auto p-1"
        @open-auto-focus.prevent
      >
        <button
          v-for="person in found.data.value"
          :key="person.id"
          type="button"
          data-person-option
          class="flex w-full flex-col rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent"
          @click="choose(person)"
        >
          <span>{{ person.label }}</span>
          <span class="text-xs text-muted-foreground">{{ person.subLabel }}</span>
        </button>
      </PopoverContent>
    </Popover>
  </div>
</template>
