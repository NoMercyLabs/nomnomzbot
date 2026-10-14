<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The send box under the feed. Send is the one primary action of the page. A message over the
// limit never leaves the browser; a failed send keeps the text so nothing is lost. A reply names
// the line it answers above the box until it is sent or canceled with the button or Escape.
// As in KMP and Twitch, ":" + 2 letters offers emotes and emoji and "@" offers the chatters here;
// the picker beside the box offers all of them to browse.
import { computed, nextTick, ref, useId, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import Cross from '~icons/nnz/cross';
import EmailReply from '~icons/nnz/email-reply';

import { chatMessageMaxLength, useChatEmotes, useSendChat, type ChatLine } from '@/api/chat';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';

import { insertAt, type Suggestion } from '../composerSuggest';
import { useComposerSuggestions } from '../useComposerSuggestions';
import ComposerSuggestions from './ComposerSuggestions.vue';
import EmotePicker from './EmotePicker.vue';

const COUNT_FROM: number = 400;

const props = defineProps<{ channelId: string | null; chatters: readonly string[] }>();
const replyTo = defineModel<ChatLine | null>('replyTo', { default: null });

const { t } = useI18n();
const sender = useSendChat(() => props.channelId);
const box = useTemplateRef<{ $el: HTMLInputElement }>('box');
const listId = useId();

const text = ref('');
const caret = ref(0);
const failed = ref(false);

const emotes = useChatEmotes(() => props.channelId);
const list = useComposerSuggestions(text, caret, () => emotes.data.value ?? [], () =>
  props.chatters);
const listOpen = computed(() => list.suggestions.value.length > 0);
const activeOption = computed<string | undefined>(() =>
  listOpen.value
    ? `${listId}-${list.active.value}`
    : undefined);

watch(replyTo, async (line) => {
  if (!line) return;

  await nextTick();
  box.value?.$el.focus();
});

function trackCaret(event: Event): void {
  const field = event.target as HTMLInputElement;

  caret.value = field.selectionStart ?? field.value.length;
}

async function place(next: { text: string; caret: number }): Promise<void> {
  text.value = next.text;
  caret.value = next.caret;
  await nextTick();
  box.value?.$el.setSelectionRange(next.caret, next.caret);
}

async function choose(suggestion: Suggestion): Promise<void> {
  const next = list.apply(suggestion);

  if (!next) return;

  await place(next);
  box.value?.$el.focus();
}

// The picker stays open after a pick, so the box takes the word without taking the focus.
function pickFromPicker(insert: string): void {
  void place(insertAt(text.value, caret.value, insert));
}

const listKeys = {
  ArrowDown: () =>
    list.move(1),
  ArrowUp: () =>
    list.move(-1),
  Tab: () =>
    void choose(list.suggestions.value[list.active.value]!),
  Enter: () =>
    void choose(list.suggestions.value[list.active.value]!),
  Escape: () =>
    list.dismiss(),
} as const satisfies Record<string, () => void>;

// An open list takes the arrows, Tab, Enter and Escape; else Escape ends a reply. Either way the key stops
// here, so the page keeps its own Escape for the chatter focus.
function onKey(event: KeyboardEvent): void {
  const listKey = listOpen.value
    ? listKeys[event.key as keyof typeof listKeys]
    : undefined;

  if (listKey) {
    event.preventDefault();
    event.stopPropagation();
    listKey();

    return;
  }

  if (event.key === 'Escape' && replyTo.value) {
    event.stopPropagation();
    replyTo.value = null;
  }
}

const tooLong = computed(() => text.value.length > chatMessageMaxLength);
const showCount = computed(() => text.value.length > COUNT_FROM);
const canSend = computed(
  () => text.value.trim() !== '' && !tooLong.value && !sender.isPending.value,
);
const problem = computed<string>(() => {
  if (tooLong.value)
    return t('dashboard.chat.composer.tooLong', { max: chatMessageMaxLength });

  return failed.value
    ? t('dashboard.chat.composer.failed')
    : '';
});

async function submit(): Promise<void> {
  if (!canSend.value) return;

  failed.value = false;

  try {
    await sender.mutateAsync({
      message: text.value,
      senderIdentity: 'you',
      replyToMessageId: replyTo.value?.id ?? null,
    });

    text.value = '';
    replyTo.value = null;
  } catch {
    failed.value = true;
  }
}
</script>

<template>
  <form class="flex w-full flex-col gap-2" @submit.prevent="submit">
    <div
      v-if="replyTo"
      data-reply-banner
      class="flex items-center gap-2 rounded-[calc(var(--radius-sm)+4px)] bg-muted py-1 ps-3 pe-1 text-sm"
    >
      <EmailReply class="size-4 flex-none text-muted-foreground" aria-hidden="true" />
      <p class="flex min-w-0 flex-1 gap-2">
        <span class="flex-none font-medium">{{ $t('dashboard.chat.reply.to', { name: replyTo.displayName }) }}</span>
        <span class="truncate text-muted-foreground">{{ replyTo.message }}</span>
      </p>
      <Button
        data-reply-cancel
        type="button"
        variant="ghost"
        size="icon-sm"
        :aria-label="$t('dashboard.chat.reply.cancel')"
        @click="replyTo = null"
      >
        <Cross class="size-4" aria-hidden="true" />
      </Button>
    </div>
    <div class="relative flex items-center gap-2">
      <ComposerSuggestions
        v-if="listOpen"
        :list-id="listId"
        :suggestions="list.suggestions.value"
        :active="list.active.value"
        @pick="choose"
      />
      <Input
        ref="box"
        v-model="text"
        data-composer
        role="combobox"
        autocomplete="off"
        aria-autocomplete="list"
        :aria-expanded="listOpen"
        :aria-controls="listOpen ? listId : undefined"
        :aria-activedescendant="activeOption"
        :aria-label="$t('dashboard.chat.composer.label')"
        :aria-invalid="tooLong"
        :placeholder="$t('dashboard.chat.composer.placeholder')"
        @input="trackCaret"
        @click="trackCaret"
        @keyup="trackCaret"
        @keydown="onKey"
      />
      <span
        v-if="showCount"
        data-composer-count
        class="flex-none text-sm text-muted-foreground tabular-nums"
        :class="{ 'text-destructive': tooLong }"
      >
        {{ $t('dashboard.chat.composer.count', { count: text.length, max: chatMessageMaxLength }) }}
      </span>
      <EmotePicker :emotes="emotes.data.value ?? []" @pick="pickFromPicker" />
      <Button type="submit" :disabled="!canSend">
        <Spinner v-if="sender.isPending.value" />
        {{ $t('dashboard.chat.composer.send') }}
      </Button>
    </div>
    <p v-if="problem" role="alert" class="text-sm text-destructive">{{ problem }}</p>
  </form>
</template>
