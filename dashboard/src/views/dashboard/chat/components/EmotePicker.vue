<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The emote and emoji picker beside the send box, laid out as Twitch and FrankerFaceZ do: recent picks, the
// emotes per source, the emoji per group, a jump rail at the side, and a search over all of them at the bottom.
// A pick goes into the box at the cursor and the picker stays open for the next one; the name of the item under
// the pointer shows below the list.
import { useLocalStorage, useMediaQuery } from '@vueuse/core';
import { computed, ref, shallowRef, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import ClockBackward from '~icons/nnz/clock-backward';
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';
import Smile from '~icons/nnz/smile';
import StarFilled from '~icons/nnz/star-filled';

import type { ChatEmoteEntry } from '@/api/chat';
import { useFavoriteEmoteChange, useFavoriteEmotes, type FavoriteEmote } from '@/api/favoriteEmotes';
import { Button } from '@/components/ui/button';
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group';
import { Popover, PopoverAnchor, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import type { Suggestion } from '../composerSuggest';
import { loadEmoji, type EmojiEntry } from '../emojiCatalog';
import { favoriteKey, pickerSections, remember, type PickerSection } from '../emotePicker';

const props = defineProps<{ emotes: readonly ChatEmoteEntry[] }>();

const emit = defineEmits<{ pick: [insert: string]; closed: [] }>();

const { t } = useI18n();
const open = ref(false);
const query = ref('');
const emoji = shallowRef<EmojiEntry[]>([]);
const recent = useLocalStorage<string[]>('nnz.chat.recent-picks', []);
const shown = ref<Suggestion | null>(null);
const { current: channel } = useCurrentChannel();
const favorites = useFavoriteEmotes();
const favoriteChange = useFavoriteEmoteChange();

async function fillEmoji(): Promise<void> {
  emoji.value = await loadEmoji();
}

watch(open, (isOpen) => {
  if (isOpen && emoji.value.length === 0) {
    void fillEmoji();
  }
});

const favoriteKeys = computed(() =>
  (favorites.data.value ?? []).map((favorite: FavoriteEmote) =>
    favoriteKey(favorite.provider, favorite.emoteId)));

const sections = computed(() =>
  pickerSections(props.emotes, emoji.value, recent.value, query.value, {
    channelName: channel.value?.displayName ?? null,
    favorites: favoriteKeys.value,
  }));

function title(section: PickerSection): string {
  if (section.name)
    return section.name;

  if (section.id.startsWith('emoji-'))
    return t(`dashboard.chat.picker.groups.${section.id.slice('emoji-'.length)}`);

  // "global-Ffz": the kind of section, then the source it comes from.
  const [kind, source] = section.id.split('-');

  return source
    ? t(`dashboard.chat.picker.${kind}`, { source: t(`asset.providers.${source}`) })
    : t(`dashboard.chat.picker.${section.id}`);
}

// An emote is starred by its provider and id; the picker lists it by its code.
const emoteByCode = computed(() =>
  new Map(props.emotes.map((emote) =>
    [emote.code, emote])));

// The emote behind a picker item, when it is one the server can star (an emoji or an id-less emote is not).
function starrable(item: Suggestion): (ChatEmoteEntry & { id: string }) | null {
  const emote = emoteByCode.value.get(item.insert);

  return item.kind === 'emote' && emote?.id
    ? { ...emote, id: emote.id }
    : null;
}

function isFavorite(item: Suggestion): boolean {
  const emote = starrable(item);

  return emote !== null && favoriteKeys.value.includes(favoriteKey(emote.provider, emote.id));
}

// Right-click, or a long press on a phone, stars an emote or takes the star off, as in FrankerFaceZ.
function toggleFavorite(item: Suggestion): void {
  const emote = starrable(item);

  if (!emote) return;

  favoriteChange.mutate({
    emote: {
      provider: emote.provider,
      emoteId: emote.id,
      code: emote.code,
      imageUrl: item.image,
    },
    favorite: !isFavorite(item),
  });
}

// The jump rail beside the list, as in Discord: one icon per section. An emote source shows its first emote,
// an emoji group a typical emoji.
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

function groupOf(section: PickerSection): string {
  return section.id.slice('emoji-'.length);
}

function isEmojiGroup(section: PickerSection): boolean {
  return groupOf(section) in groupMarks;
}

function mark(section: PickerSection): string {
  return groupMarks[groupOf(section) as keyof typeof groupMarks];
}

const list = useTemplateRef('list');
const active = ref('');
const current = computed(() =>
  active.value || sections.value[0]?.id);

// The rail marks the section at the top of the list while you scroll.
function follow(): void {
  const top = list.value?.scrollTop ?? 0;
  const passed = [...list.value?.querySelectorAll<HTMLElement>('[data-picker-section]') ?? []].filter((section) =>
    section.offsetTop <= top + 1);

  active.value = passed.at(-1)?.dataset.pickerSection ?? '';
}

function jump(id: string): void {
  document.getElementById(`picker-${id}`)?.scrollIntoView({ block: 'start' });
}

// On a phone the picker is a full-width tray as in the Twitch app: no search and no focus, so the keyboard stays
// down, and the jump rail runs along the bottom. Stoney 2026-10-14: "Remove the input from emojis on mobile".
const phone = useMediaQuery('(max-width: 767px)');

// The search sits at the bottom, so the picker opens with the cursor in it instead of on the first rail icon.
function focusSearch(event: Event): void {
  event.preventDefault();

  if (phone.value) return;

  document.querySelector<HTMLInputElement>('[data-picker-query]')?.focus();
}

// Focus handed back to the smile button draws a ring on it. The box takes it back instead; on a phone nothing
// does, so the keyboard stays down.
function returnFocus(event: Event): void {
  event.preventDefault();

  if (phone.value) return;

  emit('closed');
}

// The recent picks keep the yellow emoji, so the list finds it; the box gets the chosen version.
function pick(item: Suggestion, glyph = item.insert): void {
  recent.value = remember(recent.value, item.insert);
  emit('pick', glyph);
}

// Stoney 2026-10-14: "make variant emotes yellow and give the user the choice of which and make long press clear
// the choice and the user pick a new version". The choice is kept per emoji on this device.
const skinChoices = useLocalStorage<Record<string, string>>('nnz.chat.skin-tones', {});
const toning = ref<Suggestion | null>(null);
const toneAnchor = shallowRef<HTMLElement | null>(null);
const toneOptions = computed(() =>
  toning.value
    ? [toning.value.insert, ...toning.value.skins]
    : []);

function askTone(item: Suggestion, event: Event): void {
  toneAnchor.value = event.currentTarget as HTMLElement;
  toning.value = item;
}

function choose(item: Suggestion, event: Event): void {
  const chosen = skinChoices.value[item.insert];

  if (item.skins.length === 0 || chosen) {
    pick(item, chosen);

    return;
  }

  askTone(item, event);
}

function chooseTone(glyph: string): void {
  if (!toning.value) return;

  skinChoices.value = { ...skinChoices.value, [toning.value.insert]: glyph };
  pick(toning.value, glyph);
  toning.value = null;
}

// Right-click, or a long press on a phone: an emoji with skin tones forgets its tone and asks again; an emote
// gets its star toggled.
function secondary(item: Suggestion, event: Event): void {
  if (item.skins.length === 0) {
    toggleFavorite(item);

    return;
  }

  const { [item.insert]: _forgotten, ...kept } = skinChoices.value;

  skinChoices.value = kept;
  askTone(item, event);
}

function closeTones(isOpen: boolean): void {
  if (!isOpen) {
    toning.value = null;
  }
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        data-emote-picker
        type="button"
        variant="ghost"
        size="icon-sm"
        class="rounded-[calc(var(--radius-md)-2px)] focus-visible:ring-inset"
        :aria-label="$t('dashboard.chat.picker.open')"
      >
        <Smile class="size-5" aria-hidden="true" />
      </Button>
    </PopoverTrigger>
    <PopoverContent
      data-picker
      side="top"
      align="end"
      :collision-padding="phone ? 0 : 8"
      class="flex w-120 flex-col gap-2 p-2 max-md:w-screen max-md:rounded-none max-md:border-x-0"
      @open-auto-focus="focusSearch"
      @close-auto-focus="returnFocus"
    >
      <div class="flex h-80 gap-2 max-md:flex-col-reverse">
        <nav
          v-if="query.trim() === ''"
          class="flex w-9 shrink-0 flex-col gap-0.5 overflow-y-auto scrollbar-none max-md:w-auto max-md:flex-row
            max-md:overflow-x-auto max-md:overflow-y-hidden"
          :aria-label="$t('dashboard.chat.picker.jump')"
        >
          <button
            v-for="section in sections"
            :key="section.id"
            type="button"
            :data-picker-jump="section.id"
            :title="title(section)"
            :aria-label="title(section)"
            :aria-current="section.id === current || undefined"
            class="flex size-9 shrink-0 cursor-pointer items-center justify-center rounded-[calc(var(--radius-md)-8px)]
              text-muted-foreground hover:bg-muted hover:text-foreground aria-[current]:bg-muted
              aria-[current]:text-foreground"
            @click="jump(section.id)"
          >
            <StarFilled v-if="section.id === 'favorites'" class="size-5" aria-hidden="true" />
            <ClockBackward v-else-if="section.id === 'recent'" class="size-5" aria-hidden="true" />
            <span v-else-if="isEmojiGroup(section)" class="text-xl leading-none" aria-hidden="true">
              {{ mark(section) }}
            </span>
            <img
              v-else
              :src="section.items[0]!.image ?? undefined"
              alt=""
              class="size-6 object-contain"
            />
          </button>
        </nav>
        <div ref="list" class="relative min-w-0 flex-1 overflow-y-auto" @scroll="follow" @mouseleave="shown = null">
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
            <div class="grid grid-cols-10 gap-0.5 max-md:grid-cols-7" role="group" :aria-label="title(section)">
              <button
                v-for="item in section.items"
                :key="item.kind + item.insert"
                type="button"
                :data-picker-item="item.insert"
                :data-favorite="isFavorite(item) || undefined"
                :data-skins="item.skins.length || undefined"
                :aria-haspopup="item.skins.length > 0 && !skinChoices[item.insert] ? 'dialog' : undefined"
                :aria-label="isFavorite(item) ? $t('dashboard.chat.picker.starred', { name: item.label }) : item.label"
                class="relative flex aspect-square cursor-pointer items-center justify-center
                rounded-[calc(var(--radius-md)-8px)] hover:bg-muted focus-visible:bg-muted"
                @mouseenter="shown = item"
                @focus="shown = item"
                @click="choose(item, $event)"
                @contextmenu.prevent="secondary(item, $event)"
              >
                <span
                  v-if="item.skins.length > 0"
                  class="absolute right-0.5 bottom-0.5 size-1 rounded-full bg-muted-foreground/40"
                  aria-hidden="true"
                />
                <img
                  v-if="item.image"
                  :src="item.image"
                  alt=""
                  loading="lazy"
                  class="size-8 object-contain max-md:size-10"
                />
                <span v-else class="text-2xl leading-none max-md:text-3xl" aria-hidden="true">{{ item.insert }}</span>
              </button>
            </div>
          </section>
        </div>
      </div>
      <Popover :open="toning !== null" @update:open="closeTones">
        <PopoverAnchor :reference="toneAnchor ?? undefined" />
        <PopoverContent
          data-skin-tones
          side="top"
          class="w-auto flex-row gap-0.5 p-1"
          :aria-label="$t('dashboard.chat.picker.tones')"
        >
          <button
            v-for="glyph in toneOptions"
            :key="glyph"
            type="button"
            :data-skin-option="glyph"
            class="flex size-10 cursor-pointer items-center justify-center rounded-[calc(var(--radius-md)-4px)]
              text-2xl leading-none hover:bg-muted focus-visible:bg-muted"
            @click="chooseTone(glyph)"
          >
            {{ glyph }}
          </button>
        </PopoverContent>
      </Popover>
      <!-- A phone has no pointer to point with and a search box would raise the keyboard over the tray. -->
      <p
        v-if="!phone"
        data-picker-preview
        class="flex h-6 items-center gap-2 truncate px-1 text-sm"
        aria-live="polite"
      >
        <template v-if="shown">
          <StarFilled v-if="isFavorite(shown)" class="size-4 flex-none" :aria-label="$t('dashboard.chat.picker.favorites')" />
          <span class="truncate font-medium">{{ shown.label }}</span>
          <span class="text-xs text-muted-foreground">{{
            shown.provider
              ? $t(`asset.providers.${shown.provider}`)
              : $t(`dashboard.chat.composer.kind.${shown.kind}`)
          }}</span>
        </template>
        <span v-else class="text-muted-foreground">{{ $t('dashboard.chat.picker.hint') }}</span>
      </p>
      <InputGroup v-if="!phone" data-picker-search class="rounded-[calc(var(--radius-md)-8px)]">
        <InputGroupAddon>
          <SearchMagnifyingGlass />
        </InputGroupAddon>
        <InputGroupInput
          v-model="query"
          data-picker-query
          type="search"
          :placeholder="$t('dashboard.chat.picker.search')"
          :aria-label="$t('dashboard.chat.picker.search')"
        />
      </InputGroup>
    </PopoverContent>
  </Popover>
</template>
