<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The emote and emoji picker beside the send box, laid out as Twitch and FrankerFaceZ do: recent picks, the
// emotes per source, the emoji per group, a jump bar, and a search over all of them. A pick goes into the box
// at the cursor and the picker stays open for the next one; the name of the item under the pointer shows below.
import { useLocalStorage } from '@vueuse/core';
import { computed, ref, shallowRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import ClockBackward from '~icons/nnz/clock-backward';
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';
import Smile from '~icons/nnz/smile';

import type { ChatEmoteEntry } from '@/api/chat';
import { Button } from '@/components/ui/button';
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

import type { Suggestion } from '../composerSuggest';
import { loadEmoji, type EmojiEntry } from '../emojiCatalog';
import { pickerSections, remember, type PickerSection } from '../emotePicker';

const props = defineProps<{ emotes: readonly ChatEmoteEntry[] }>();

const emit = defineEmits<{ pick: [insert: string] }>();

const { t } = useI18n();
const open = ref(false);
const query = ref('');
const emoji = shallowRef<EmojiEntry[]>([]);
const recent = useLocalStorage<string[]>('nnz.chat.recent-picks', []);
const shown = ref<Suggestion | null>(null);

async function fillEmoji(): Promise<void> {
  emoji.value = await loadEmoji();
}

watch(open, (isOpen) => {
  if (isOpen && emoji.value.length === 0) {
    void fillEmoji();
  }
});

const sections = computed(() =>
  pickerSections(props.emotes, emoji.value, recent.value, query.value));

function title(section: PickerSection): string {
  if (section.id.startsWith('emoji-'))
    return t(`dashboard.chat.picker.groups.${section.id.slice('emoji-'.length)}`);

  if (section.id === 'recent' || section.id === 'results')
    return t(`dashboard.chat.picker.${section.id}`);

  return t(`dashboard.chat.asset.providers.${section.id}`);
}

// The jump bar fits one row as in Discord and Slack: a short name per emote source, one emoji per emoji group.
const groupMarks = {
  smileys: '😀',
  people: '👋',
  animals: '🐱',
  food: '🍔',
  travel: '✈️',
  activities: '⚽',
  objects: '💡',
  symbols: '❤️',
  flags: '🏁',
} as const satisfies Record<string, string>;

function mark(section: PickerSection): string {
  const group = section.id.slice('emoji-'.length);

  if (group in groupMarks)
    return groupMarks[group as keyof typeof groupMarks];

  return t(`dashboard.chat.picker.short.${section.id}`);
}

function jump(id: string): void {
  document.getElementById(`picker-${id}`)?.scrollIntoView({ block: 'start' });
}

function pick(item: Suggestion): void {
  recent.value = remember(recent.value, item.insert);
  emit('pick', item.insert);
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        data-emote-picker
        type="button"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.chat.picker.open')"
      >
        <Smile class="size-5" aria-hidden="true" />
      </Button>
    </PopoverTrigger>
    <PopoverContent data-picker side="top" align="end" class="flex w-120 flex-col gap-2 p-2">
      <InputGroup data-picker-search class="rounded-[calc(var(--radius-md)-8px)]">
        <InputGroupAddon>
          <SearchMagnifyingGlass />
        </InputGroupAddon>
        <InputGroupInput
          v-model="query"
          type="search"
          :placeholder="$t('dashboard.chat.picker.search')"
          :aria-label="$t('dashboard.chat.picker.search')"
        />
      </InputGroup>
      <nav
        v-if="query.trim() === ''"
        class="flex items-center justify-between"
        :aria-label="$t('dashboard.chat.picker.jump')"
      >
        <button
          v-for="section in sections"
          :key="section.id"
          type="button"
          :data-picker-jump="section.id"
          :title="title(section)"
          :aria-label="title(section)"
          class="flex h-7 min-w-7 cursor-pointer items-center justify-center rounded-[calc(var(--radius-md)-8px)]
            px-1.5 text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground"
          @click="jump(section.id)"
        >
          <ClockBackward v-if="section.id === 'recent'" class="size-4" aria-hidden="true" />
          <span v-else aria-hidden="true" :class="{ 'text-base leading-none': section.id.startsWith('emoji-') }">
            {{ mark(section) }}
          </span>
        </button>
      </nav>
      <div class="h-80 overflow-y-auto" @mouseleave="shown = null">
        <p v-if="sections.length === 0 || sections[0]!.items.length === 0" class="p-4 text-center text-sm text-muted-foreground">
          {{ $t('dashboard.chat.picker.none') }}
        </p>
        <section
          v-for="section in sections"
          :id="`picker-${section.id}`"
          :key="section.id"
          :data-picker-section="section.id"
        >
          <h3 class="sticky top-0 z-10 bg-popover px-1 py-1 text-xs font-medium text-muted-foreground">
            {{ title(section) }}
          </h3>
          <div class="grid grid-cols-10 gap-0.5" role="group" :aria-label="title(section)">
            <button
              v-for="item in section.items"
              :key="item.kind + item.insert"
              type="button"
              :data-picker-item="item.insert"
              :aria-label="item.label"
              class="flex aspect-square cursor-pointer items-center justify-center
                rounded-[calc(var(--radius-md)-8px)] hover:bg-muted focus-visible:bg-muted"
              @mouseenter="shown = item"
              @focus="shown = item"
              @click="pick(item)"
            >
              <img
                v-if="item.image"
                :src="item.image"
                alt=""
                loading="lazy"
                class="size-8 object-contain"
              />
              <span v-else class="text-2xl leading-none" aria-hidden="true">{{ item.insert }}</span>
            </button>
          </div>
        </section>
      </div>
      <p data-picker-preview class="flex h-6 items-center gap-2 truncate px-1 text-sm" aria-live="polite">
        <template v-if="shown">
          <span class="truncate font-medium">{{ shown.label }}</span>
          <span class="text-xs text-muted-foreground">{{
            shown.provider
              ? $t(`dashboard.chat.asset.providers.${shown.provider}`)
              : $t(`dashboard.chat.composer.kind.${shown.kind}`)
          }}</span>
        </template>
        <span v-else class="text-muted-foreground">{{ $t('dashboard.chat.picker.hint') }}</span>
      </p>
    </PopoverContent>
  </Popover>
</template>
