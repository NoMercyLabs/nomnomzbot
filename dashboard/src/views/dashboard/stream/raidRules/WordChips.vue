<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A word list of any length as chips. A long list shows its first words and a count; "Show all" opens it.
// Enter or a comma adds a word, a pasted list of comma separated words adds each one.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { Collapsible, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Input } from '@/components/ui/input';
import ChevronDown from '~icons/nnz/chevron-down';
import Cross from '~icons/nnz/cross';

const props = defineProps<{ canEdit: boolean }>();

const FOLDED_COUNT = 12;

const { t } = useI18n();
const words = defineModel<string[]>({ required: true });
const entry = ref('');
const open = ref(false);

const shown = computed(() => {
  if (open.value)
    return words.value;

  return words.value.slice(0, FOLDED_COUNT);
});

const folds = computed(() => words.value.length > FOLDED_COUNT);

function add(): void {
  const typed = entry.value
    .split(',')
    .map((word) => word.trim())
    .filter((word) => word !== '');

  entry.value = '';

  const known = new Set(words.value.map((word) => word.toLowerCase()));
  const fresh = typed.filter((word) => {
    const isNew = !known.has(word.toLowerCase());

    known.add(word.toLowerCase());

    return isNew;
  });

  if (fresh.length === 0) return;

  words.value = words.value.concat(fresh);
}

// A comma at the end of what was typed closes that word, so typing "a," then "b," works without Enter.
watch(entry, (typed) => {
  if (typed.endsWith(',')) {
    add();
  }
});

function remove(word: string): void {
  words.value = words.value.filter((existing) => existing !== word);
}
</script>

<template>
  <Collapsible v-model:open="open" class="flex min-w-0 flex-col gap-2">
    <p data-words-count class="text-sm text-muted-foreground">
      {{ t('dashboard.stream.raids.wordCount', { count: words.length }, words.length) }}
    </p>
    <div
      v-if="words.length > 0"
      class="flex max-h-48 flex-wrap gap-1.5 overflow-y-auto"
    >
      <span
        v-for="word in shown"
        :key="word"
        data-word
        class="inline-flex items-center gap-1 rounded-lg border border-border bg-muted py-0.5 pl-2.5 text-sm"
        :class="props.canEdit ? 'pr-0.5' : 'pr-2.5'"
      >
        {{ word }}
        <button
          v-if="props.canEdit"
          type="button"
          class="inline-flex size-7 items-center justify-center rounded-md text-muted-foreground hover:bg-foreground/10 hover:text-foreground"
          :aria-label="t('dashboard.stream.raids.removeWord', { word })"
          @click="remove(word)"
        >
          <Cross class="size-5" />
        </button>
      </span>
    </div>
    <CollapsibleTrigger
      v-if="folds"
      data-words-toggle
      class="inline-flex h-8 w-fit items-center gap-1 rounded-md px-3 text-sm font-medium hover:bg-accent hover:text-accent-foreground"
    >
      {{ open ? t('dashboard.stream.raids.showFewer') : t('dashboard.stream.raids.showAll') }}
      <ChevronDown
        class="size-4 transition-transform"
        :class="{ 'rotate-180': open }"
      />
    </CollapsibleTrigger>
    <Input
      v-if="props.canEdit"
      v-model="entry"
      data-word-entry
      :aria-label="t('dashboard.stream.raids.addWord')"
      :placeholder="t('dashboard.stream.raids.addWord')"
      @keydown.enter.prevent="add"
    />
  </Collapsible>
</template>
