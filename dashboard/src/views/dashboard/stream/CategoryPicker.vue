<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The category: what it is now, a search that Twitch answers, and the categories of the last streams.
import { refDebounced } from '@vueuse/core';
import { computed, ref, type Ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCategorySearch, type Category } from '@/api/stream';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

const props = defineProps<{ channelId: string | null; recent: string[] }>();
const { t } = useI18n();
const game = defineModel<string>({ required: true });

const search = ref('');
const asked: Readonly<Ref<string>> = refDebounced(search, 250);
const results = useCategorySearch(() => props.channelId, asked);
const found = computed<Category[]>(() => results.data.value ?? []);
const searching = computed(() => asked.value.trim() !== '' && results.isPending.value);

const emit = defineEmits<{ picked: [category: Category] }>();

function pick(name: string): void {
  game.value = name;
  search.value = '';
}

function pickFound(category: Category): void {
  pick(category.name);
  emit('picked', category);
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <p class="text-sm font-medium">{{ game }}</p>
    <Input
      v-model="search"
      data-field="category-search"
      :placeholder="t('dashboard.stream.info.categorySearch')"
    />
    <div
      v-if="asked.trim() !== ''"
      class="flex flex-col rounded-[12px] border border-border bg-muted p-1"
    >
      <p v-if="searching" class="px-3 py-2 text-sm text-muted-foreground">
        {{ t('dashboard.stream.info.categorySearching') }}
      </p>
      <p v-else-if="found.length === 0" class="px-3 py-2 text-sm text-muted-foreground">
        {{ t('dashboard.stream.info.categoryNone') }}
      </p>
      <button
        v-for="category in found"
        :key="category.id"
        type="button"
        data-category-result
        class="min-h-9 rounded-[8px] px-3 py-1.5 text-left text-sm hover:bg-foreground/5"
        @click="pickFound(category)"
      >
        {{ category.name }}
      </button>
    </div>
    <div v-if="recent.length > 0" class="flex flex-wrap items-center gap-1.5">
      <span class="text-sm text-muted-foreground">{{
        t('dashboard.stream.info.recentCategories')
      }}</span>
      <Button
        v-for="name in recent"
        :key="name"
        variant="outline"
        size="sm"
        data-recent-category
        @click="pick(name)"
      >
        {{ name }}
      </Button>
    </div>
  </div>
</template>
