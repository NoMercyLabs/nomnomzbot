<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The category: what it is now, a search that Twitch answers, and the categories of the last streams.
import { refDebounced } from '@vueuse/core'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useCategorySearch, type Category } from '@/api/stream'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const props = defineProps<{ channelId: string | null; recent: string[] }>()
const { t } = useI18n()
const game = defineModel<string>({ required: true })

const search = ref('')
const asked: Readonly<Ref<string>> = refDebounced(search, 250)
const results = useCategorySearch(() => props.channelId, asked)
const found: ComputedRef<Category[]> = computed(() => results.data.value ?? [])
const searching = computed(() => asked.value.trim() !== '' && results.isPending.value)

function pick(name: string): void {
  game.value = name
  search.value = ''
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
        @click="pick(category.name)"
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
