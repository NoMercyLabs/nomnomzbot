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
import { computed, nextTick, ref, shallowRef, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronDown from '~icons/nnz/chevron-down';
import ClockBackward from '~icons/nnz/clock-backward';
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';
import Smile from '~icons/nnz/smile';
import Star from '~icons/nnz/star';
import StarFilled from '~icons/nnz/star-filled';

import { useCurrentUser } from '@/api/auth';
import type { ChatEmoteEntry } from '@/api/chat';
import { useFavoriteEmoteChange, useFavoriteEmotes, type FavoriteEmote } from '@/api/favoriteEmotes';
import { Button } from '@/components/ui/button';
import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group';
import { Popover, PopoverAnchor, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Separator } from '@/components/ui/separator';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import type { Suggestion } from '../composerSuggest';
import { loadEmoji, type EmojiEntry } from '../emojiCatalog';
import PickerToneMenu from './PickerToneMenu.vue';
import {
  emojiProvider,
  favoriteKey,
  pickerSections,
  starId,
  remember,
  skinTone,
  type PickerContext,
  type PickerSection,
} from '../emotePicker';

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

const me = useCurrentUser();
const context = computed<PickerContext>(() => ({
  channelName: channel.value?.displayName ?? null,
  channelAvatar: channel.value?.profileImageUrl ?? null,
  ownAvatar: me.data.value?.profileImageUrl ?? null,
  ownName: me.data.value?.displayName ?? null,
  favorites: favoriteKeys.value,
}));

// Every section as it is without a search: the rail keeps all of them, so it never jumps while you type.
const browse = computed(() =>
  pickerSections(props.emotes, emoji.value, recent.value, '', context.value));

const sections = computed(() =>
  query.value.trim() === ''
    ? browse.value
    : pickerSections(props.emotes, emoji.value, recent.value, query.value, context.value));

const shownSections = computed(() =>
  sections.value.filter((section) =>
    section.items.length > 0));

// Stoney 2026-10-14: "instead of removing the tabs, make them grayed out and locked and keep the remaining tabs
// enabled". A section without a match stays on the rail, grayed out and not clickable.
const matched = computed(() =>
  new Set(shownSections.value.map((section) =>
    section.id)));

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

type StarTarget = {
  provider: string;
  emoteId: string;
  code: string;
};

// What the server stars for a picker item: an emote by its source and id (its code when it came without one), an
// emoji by its glyph.
function starrable(item: Suggestion): StarTarget | null {
  if (item.kind === 'emoji')
    return {
      provider: emojiProvider,
      emoteId: item.insert,
      code: item.label,
    };

  const emote = emoteByCode.value.get(item.insert);

  return emote
    ? {
        provider: emote.provider,
        emoteId: starId(emote),
        code: emote.code,
      }
    : null;
}

function isFavorite(item: Suggestion): boolean {
  const target = starrable(item);

  if (!target)
    return false;

  return favoriteKeys.value.includes(favoriteKey(target.provider, target.emoteId));
}

// Right-click, or a long press on a phone, stars an emote or emoji or takes the star off, as in FrankerFaceZ.
function toggleFavorite(item: Suggestion): void {
  const target = starrable(item);

  if (!target) return;

  favoriteChange.mutate({
    emote: { ...target, imageUrl: item.image },
    favorite: !isFavorite(item),
  });
}

// A channel's section names the sources its emotes come from in its heading, as the Twitch picker does. A source
// section already says it in its title.
function sourceOf(section: PickerSection): string {
  if (!['own', 'channel', 'channelExtras'].includes(section.id) && !section.id.startsWith('owner-'))
    return '';

  const providers = new Set(section.items.map((item) =>
    item.provider ?? ''));

  return [...providers].filter(Boolean).map((provider) =>
    t(`asset.providers.${provider}`)).join(', ');
}

// The picture of a section: the channel's avatar, else its first emote.
function cover(section: PickerSection): string | undefined {
  return section.avatar ?? section.items[0]?.image ?? undefined;
}

// A heading folds its section away; the folded ones are kept on this device.
const collapsed = useLocalStorage<string[]>('nnz.chat.picker-collapsed', []);

function isCollapsed(section: PickerSection): boolean {
  return collapsed.value.includes(section.id);
}

function toggle(id: string): void {
  collapsed.value = collapsed.value.includes(id)
    ? collapsed.value.filter((folded) =>
        folded !== id)
    : [...collapsed.value, id];
}

// The jump rail beside the list, as in Discord: one icon per section. A channel shows its avatar, an emote source
// its first emote, an emoji group a typical emoji.
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
  active.value || shownSections.value[0]?.id);

// The rail marks the section at the top of the list while you scroll.
function follow(): void {
  const top = list.value?.scrollTop ?? 0;
  const passed = [...list.value?.querySelectorAll<HTMLElement>('[data-picker-section]') ?? []].filter((section) =>
    section.offsetTop <= top + 1);

  active.value = passed.at(-1)?.dataset.pickerSection ?? '';
}

// A jump to a folded section unfolds it first.
async function jump(id: string): Promise<void> {
  if (collapsed.value.includes(id)) {
    toggle(id);
    await nextTick();
  }

  document.getElementById(`picker-${id}`)?.scrollIntoView({ block: 'start' });
}

// On a phone the picker is a full-width tray as in the Twitch app: no search and no focus, so the keyboard stays
// down, and the jump rail runs along the bottom. Stoney 2026-10-14: "Remove the input from emojis on mobile".
// Narrow AND touch, the `phone:` variant in main.css: a narrow desktop window (the popout dock) keeps the desktop picker.
const phone = useMediaQuery('(max-width: 767px) and (pointer: coarse)');

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

// Stoney 2026-10-14: "someone needs to be able to choose a skin tone once and the picker needs to swap them to
// that skin tone for all emojis". One tone, chosen beside the search, turns every emoji to it; 0 is yellow.
const skinToneForAll = useLocalStorage<number>('nnz.chat.skin-tone', 0);
// "make long press clear the choice and the user pick a new version": a long press gives one emoji a tone of
// its own, kept per emoji on this device, over the tone for all.
const skinChoices = useLocalStorage<Record<string, string>>('nnz.chat.skin-tones', {});
const toning = ref<Suggestion | null>(null);
const toneAnchor = shallowRef<HTMLElement | null>(null);
const toneOptions = computed(() =>
  toning.value
    ? [toning.value.insert, ...toning.value.skins]
    : []);

// The emoji as it shows and goes into the box: its own tone, else the tone for all.
function glyph(item: Suggestion): string {
  return skinChoices.value[item.insert] ?? skinTone(item, skinToneForAll.value);
}

function askTone(item: Suggestion, event: Event): void {
  toneAnchor.value = event.currentTarget as HTMLElement;
  toning.value = item;
}

function choose(item: Suggestion): void {
  pick(item, glyph(item));
}

function chooseTone(toned: string): void {
  if (!toning.value) return;

  skinChoices.value = { ...skinChoices.value, [toning.value.insert]: toned };
  pick(toning.value, toned);
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

// A toned emoji's right-click asks for its tone, so its star lives in the tone popup.
function starToned(): void {
  if (!toning.value) return;

  toggleFavorite(toning.value);
  toning.value = null;
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
      class="flex w-120 flex-col gap-2 p-2 phone:w-screen phone:rounded-none phone:border-x-0"
      @open-auto-focus="focusSearch"
      @close-auto-focus="returnFocus"
    >
      <div class="flex h-80 gap-2 phone:flex-col-reverse">
        <div data-picker-rail class="flex min-w-0 shrink-0 gap-1 phone:items-center">
          <nav
            class="flex w-9 shrink-0 flex-col gap-0.5 overflow-y-auto scrollbar-none phone:w-auto phone:min-w-0
            phone:flex-1 phone:flex-row phone:overflow-x-auto phone:overflow-y-hidden"
            :aria-label="$t('dashboard.chat.picker.jump')"
          >
            <button
              v-for="section in browse"
              :key="section.id"
              type="button"
              :data-picker-jump="section.id"
              :title="title(section)"
              :aria-label="title(section)"
              :aria-current="section.id === current || undefined"
              :disabled="!matched.has(section.id)"
              class="flex size-9 shrink-0 cursor-pointer items-center justify-center rounded-[calc(var(--radius-md)-8px)]
              text-muted-foreground hover:bg-muted hover:text-foreground disabled:cursor-not-allowed
              disabled:opacity-40 disabled:grayscale disabled:hover:bg-transparent aria-[current]:bg-muted
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
                :src="cover(section)"
                alt=""
                class="size-6 object-contain"
                :class="{ 'rounded-full object-cover': section.avatar }"
              />
            </button>
          </nav>
          <PickerToneMenu v-if="phone" v-model="skinToneForAll" />
        </div>
        <div
          ref="list"
          class="relative min-w-0 flex-1 overflow-y-auto phone:snap-y phone:snap-mandatory phone:scroll-pt-7"
          @scroll="follow"
          @mouseleave="shown = null"
        >
          <p v-if="shownSections.length === 0" class="p-4 text-center text-sm text-muted-foreground">
            {{ $t('dashboard.chat.picker.none') }}
          </p>
          <section
            v-for="section in shownSections"
            :id="`picker-${section.id}`"
            :key="section.id"
            :data-picker-section="section.id"
          >
            <h3 class="sticky top-0 z-10 bg-popover">
              <button
                type="button"
                :data-picker-collapse="section.id"
                :aria-expanded="!isCollapsed(section)"
                class="flex h-7 w-full cursor-pointer items-center gap-2 rounded-[calc(var(--radius-md)-8px)] px-1
                  text-xs font-medium text-muted-foreground hover:text-foreground"
                @click="toggle(section.id)"
              >
                <img v-if="section.avatar" :src="section.avatar" alt="" class="size-4 rounded-full object-cover" />
                <span class="truncate">{{ title(section) }}</span>
                <span v-if="sourceOf(section)" class="shrink-0 font-normal">{{ sourceOf(section) }}</span>
                <ChevronDown
                  class="ms-auto size-4 shrink-0 transition-transform motion-reduce:transition-none"
                  :class="{ '-rotate-90': isCollapsed(section) }"
                  aria-hidden="true"
                />
              </button>
            </h3>
            <div
              v-if="!isCollapsed(section)"
              class="grid grid-cols-10 gap-0.5 phone:grid-cols-7"
              role="group"
              :aria-label="title(section)"
            >
              <button
                v-for="item in section.items"
                :key="item.kind + item.insert"
                type="button"
                :data-picker-item="item.insert"
                :data-favorite="isFavorite(item) || undefined"
                :data-skins="item.skins.length || undefined"
                :aria-label="isFavorite(item) ? $t('dashboard.chat.picker.starred', { name: item.label }) : item.label"
                class="relative flex aspect-square cursor-pointer items-center justify-center phone:snap-start
                rounded-[calc(var(--radius-md)-8px)] hover:bg-muted focus-visible:bg-muted"
                @mouseenter="shown = item"
                @focus="shown = item"
                @click="choose(item)"
                @contextmenu.prevent="secondary(item, $event)"
              >
                <StarFilled
                  v-if="isFavorite(item)"
                  data-favorite-badge
                  class="absolute top-0.5 right-0.5 size-2.5 text-muted-foreground"
                  aria-hidden="true"
                />
                <img
                  v-if="item.image"
                  :src="item.image"
                  alt=""
                  loading="lazy"
                  class="size-8 object-contain phone:size-10"
                />
                <span v-else class="text-2xl leading-none phone:text-3xl" aria-hidden="true">{{ glyph(item) }}</span>
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
          <Separator orientation="vertical" class="mx-0.5 h-8 self-center" />
          <button
            v-if="toning"
            type="button"
            data-tone-star
            :aria-pressed="isFavorite(toning)"
            :aria-label="$t('dashboard.chat.picker.star', { name: toning.label })"
            class="flex size-10 cursor-pointer items-center justify-center rounded-[calc(var(--radius-md)-4px)]
              text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:bg-muted"
            @click="starToned"
          >
            <StarFilled v-if="isFavorite(toning)" class="size-5" aria-hidden="true" />
            <Star v-else class="size-5" aria-hidden="true" />
          </button>
        </PopoverContent>
      </Popover>
      <!-- A phone has no pointer to point with and a search box would raise the keyboard over the tray. -->
      <p
        v-if="!phone"
        data-picker-preview
        class="flex h-6 min-w-0 items-center gap-2 px-1 text-sm"
        aria-live="polite"
      >
        <template v-if="shown">
          <StarFilled v-if="isFavorite(shown)" class="size-4 flex-none" :aria-label="$t('dashboard.chat.picker.favorites')" />
          <span class="truncate font-medium">{{ shown.label }}</span>
          <span class="shrink-0 text-xs text-muted-foreground">{{
            shown.provider
              ? $t(`asset.providers.${shown.provider}`)
              : $t(`dashboard.chat.composer.kind.${shown.kind}`)
          }}</span>
        </template>
        <span v-else class="truncate text-muted-foreground">{{ $t('dashboard.chat.picker.hint') }}</span>
      </p>
      <div v-if="!phone" class="flex items-center gap-2">
        <InputGroup data-picker-search class="flex-1 rounded-[calc(var(--radius-md)-8px)]">
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
        <PickerToneMenu v-model="skinToneForAll" />
      </div>
    </PopoverContent>
  </Popover>
</template>
