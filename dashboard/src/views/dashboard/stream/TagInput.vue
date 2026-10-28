<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Tags of the stream: Enter adds one, the x removes one and a line right under offers Undo.
import X from '~icons/nnz/cross';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

// Twitch allows at most 10 tags per stream.
const MAX_TAGS: number = 10;

const { t } = useI18n();
const tags = defineModel<string[]>({ required: true });
const entry = ref('');
const removed = ref<{ tag: string; index: number } | null>(null);

function add(): void {
  const tag = entry.value.trim();

  entry.value = '';
  const known = tags.value.some((existing) => existing.toLowerCase() === tag.toLowerCase());

  if (tag === '' || known || tags.value.length >= MAX_TAGS) return;

  tags.value = [...tags.value, tag];
  removed.value = null;
}

function remove(index: number): void {
  removed.value = { tag: tags.value[index] as string, index };
  tags.value = tags.value.filter((_tag, at) => at !== index);
}

function undo(): void {
  if (removed.value === null) return;

  const next: string[] = [...tags.value];

  next.splice(Math.min(removed.value.index, next.length), 0, removed.value.tag);
  tags.value = next;
  removed.value = null;
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div v-if="tags.length > 0" class="flex flex-wrap gap-1.5">
      <span
        v-for="(tag, index) in tags"
        :key="tag"
        class="inline-flex items-center gap-1 rounded-[8px] border border-border bg-muted py-0.5 pr-0.5 pl-2 text-sm"
      >
        {{ tag }}
        <button
          type="button"
          class="relative inline-flex size-6 items-center after:absolute after:-inset-1.5 after:content-[''] justify-center rounded-[6px] text-muted-foreground hover:bg-foreground/10 hover:text-foreground"
          :aria-label="t('dashboard.stream.info.tagRemove', { tag })"
          @click="remove(index)"
        >
          <X class="size-3.5" />
        </button>
      </span>
    </div>
    <Input
      v-model="entry"
      data-field="tag-input"
      :placeholder="t('dashboard.stream.info.tagPlaceholder')"
      :disabled="tags.length >= MAX_TAGS"
      @keydown.enter.prevent="add"
    />
    <p class="text-sm text-muted-foreground">
      {{ t('dashboard.stream.info.tagLimit', { count: tags.length }) }}
    </p>
    <p v-if="removed" class="flex items-center gap-2 text-sm">
      {{ t('dashboard.stream.info.tagRemoved', { tag: removed.tag }) }}
      <Button variant="ghost" size="sm" @click="undo">{{
        t('dashboard.stream.info.tagUndo')
      }}</Button>
    </p>
  </div>
</template>
