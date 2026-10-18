<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Picks one person the bot knows by typing a name. The bot answers with real people; there is no free id field.
import Cross from '~icons/nnz/cross';
import { ref } from 'vue';

import { useViewerSearch, type ViewerOption } from '@/api/community';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

defineProps<{ picked: ViewerOption | null; label: string }>();

const emit = defineEmits<{ pick: [person: ViewerOption | null] }>();

const { channelId } = useCurrentChannel();
const search = ref('');
const found = useViewerSearch(channelId, search);

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
    <template v-else>
      <Input v-model="search" data-person-search :aria-label="label" :placeholder="$t('dashboard.settings.permissions.person.placeholder')" />
      <ul
        v-if="search.trim() !== '' && (found.data.value?.length ?? 0) > 0"
        class="absolute z-10 mt-1 max-h-56 w-full overflow-auto rounded-md border bg-popover p-1 shadow-md"
      >
        <li v-for="person in found.data.value" :key="person.id">
          <button
            type="button"
            data-person-option
            class="flex w-full flex-col rounded-sm px-2 py-1.5 text-left text-sm hover:bg-accent"
            @click="choose(person)"
          >
            <span>{{ person.label }}</span>
            <span class="text-xs text-muted-foreground">{{ person.subLabel }}</span>
          </button>
        </li>
      </ul>
    </template>
  </div>
</template>
