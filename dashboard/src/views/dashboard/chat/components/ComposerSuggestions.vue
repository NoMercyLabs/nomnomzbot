<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The list floating above the send box while a word is being completed. A press keeps the cursor in the box.
import type { Suggestion } from '../composerSuggest';

defineProps<{
  listId: string;
  suggestions: readonly Suggestion[];
  active: number;
}>();

const emit = defineEmits<{ pick: [suggestion: Suggestion] }>();
</script>

<template>
  <ul
    :id="listId"
    data-suggestions
    role="listbox"
    :aria-label="$t('dashboard.chat.composer.suggestions')"
    class="absolute inset-s-0 bottom-full z-20 mb-2 w-80 max-h-80 overflow-y-auto rounded-md border bg-popover p-1 text-popover-foreground shadow-md"
  >
    <li
      v-for="(suggestion, at) in suggestions"
      :id="`${listId}-${at}`"
      :key="suggestion.kind + suggestion.insert"
      :data-suggestion="suggestion.insert"
      role="option"
      :aria-selected="at === active"
      class="flex cursor-pointer items-center gap-2 rounded-[calc(var(--radius-md)-4px)] px-2 py-1.5 text-sm"
      :class="{ 'bg-muted': at === active }"
      @mousedown.prevent
      @click="emit('pick', suggestion)"
    >
      <img
        v-if="suggestion.image"
        :src="suggestion.image"
        alt=""
        class="size-6 flex-none object-contain"
      />
      <span v-else-if="suggestion.kind === 'emoji'" class="flex size-6 flex-none items-center justify-center text-lg" aria-hidden="true">{{ suggestion.insert }}</span>
      <span class="min-w-0 flex-1 truncate">{{ suggestion.label }}</span>
      <span class="flex-none text-xs text-muted-foreground">{{
        suggestion.provider
          ? $t(`dashboard.chat.asset.providers.${suggestion.provider}`)
          : $t(`dashboard.chat.composer.kind.${suggestion.kind}`)
      }}</span>
    </li>
  </ul>
</template>
