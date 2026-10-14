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
// the picker beside the box offers all of them to browse. An emote shows as its image in the box, as on
// Twitch and 7TV; the message that leaves is plain text with the emote codes.
import Document from '@tiptap/extension-document';
import Paragraph from '@tiptap/extension-paragraph';
import Text from '@tiptap/extension-text';
import { UndoRedo } from '@tiptap/extensions';
import type { Editor, JSONContent } from '@tiptap/core';
import type { Node as ProseMirrorNode } from '@tiptap/pm/model';
import { EditorContent, useEditor } from '@tiptap/vue-3';
import { computed, nextTick, ref, useId, watch, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import Cross from '~icons/nnz/cross';
import EmailReply from '~icons/nnz/email-reply';

import { chatMessageMaxLength, useChatEmotes, useSendChat, type ChatLine } from '@/api/chat';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
import { merge } from '@/lib/array';

import { ChatInput, EmoteNode, type EmoteImage } from '../chatEditor';
import { emoteSuggestion, type Suggestion } from '../composerSuggest';
import { useComposerSuggestions } from '../useComposerSuggestions';
import ComposerSuggestions from './ComposerSuggestions.vue';
import EmotePicker from './EmotePicker.vue';

const COUNT_FROM: number = 400;

const props = defineProps<{ channelId: string | null; chatters: readonly string[] }>();
const replyTo = defineModel<ChatLine | null>('replyTo', { default: null });

const { t } = useI18n();
const sender = useSendChat(() => props.channelId);
const listId = useId();

const text = ref('');
const caret = ref(0);
const failed = ref(false);

const emotes = useChatEmotes(() => props.channelId);
const list = useComposerSuggestions(text, caret, () => emotes.data.value ?? [], () =>
  props.chatters);
const listOpen = computed(() => list.suggestions.value.length > 0);

// Every emote this sender can use, by code, with the image the box shows for it.
const images = computed(() =>
  new Map((emotes.data.value ?? []).flatMap((emote) => {
    const src = emoteSuggestion(emote).image;

    return src
      ? [[emote.code, { code: emote.code, src }] as const]
      : [];
  })));

function lookup(word: string): EmoteImage | undefined {
  return images.value.get(word);
}

// An emote counts as its code: that is what the message carries.
function leafText(node: ProseMirrorNode): string {
  return String(node.attrs.code ?? '');
}

function textUpTo(editor: Editor, position: number): string {
  return editor.state.doc.textBetween(0, position, '', leafText);
}

function sync({ editor }: { editor: Editor }): void {
  text.value = textUpTo(editor, editor.state.doc.content.size);
  caret.value = textUpTo(editor, editor.state.selection.from).length;
}

const editor = useEditor({
  extensions: [
    Document.extend({ content: 'paragraph' }),
    Paragraph,
    Text,
    EmoteNode,
    UndoRedo,
    ChatInput.configure({
      lookup,
      onKey: (event) =>
        onKey(event),
    }),
  ],
  onUpdate: sync,
  onSelectionUpdate: sync,
});

// A word as it goes into the box at a position: an emote as its image, anything else as text, with a space
// after it unless one follows already.
function piece(box: Editor, at: number, insert: string): JSONContent[] {
  const emote = lookup(insert);
  const word = emote
    ? { type: 'emote', attrs: emote }
    : { type: 'text', text: insert };
  const tail = box.state.doc.textBetween(at, box.state.doc.content.size, '', leafText);

  return /^\s/u.test(tail)
    ? [word]
    : [word, { type: 'text', text: ' ' }];
}

// The pick replaces the typed ":" or "@" word.
function choose(suggestion: Suggestion): void {
  const found = list.token.value;
  const box = editor.value;

  if (!found || !box) return;

  const to = box.state.selection.from;
  const typed = { from: to - found.query.length - 1, to };

  const word = piece(box, to, suggestion.insert);

  box.chain().focus().deleteRange(typed).insertContent(word).run();
}

// The picker stays open after a pick, so the box takes the word without taking the focus. The pick is its own
// word: a space goes before it when it would touch the word in front.
function pickFromPicker(insert: string): void {
  const box = editor.value;

  if (!box) return;

  const at = box.state.selection.from;
  const head = textUpTo(box, at);
  const gap: JSONContent[] = head === '' || /\s$/u.test(head)
    ? []
    : [{ type: 'text', text: ' ' }];
  const word = piece(box, at, insert);

  box.commands.insertContentAt(at, merge(gap, word));
}

// The closed picker hands the focus back here. When the chat page is leaving, the box is already gone.
function focusBox(): void {
  if (!editor.value || editor.value.isDestroyed) return;

  editor.value.commands.focus('end');
}

watch(replyTo, async (line) => {
  if (!line) return;

  await nextTick();
  editor.value?.commands.focus('end');
});

const listKeys = {
  ArrowDown: () =>
    list.move(1),
  ArrowUp: () =>
    list.move(-1),
  Tab: () =>
    choose(list.suggestions.value[list.active.value]!),
  Enter: () =>
    choose(list.suggestions.value[list.active.value]!),
  Escape: () =>
    list.dismiss(),
} as const satisfies Record<string, () => void>;

// An open list takes the arrows, Tab, Enter and Escape; else Enter sends and Escape ends a reply. Either way the
// key stops here, so the page keeps its own Escape for the chatter focus.
function onKey(event: KeyboardEvent): boolean {
  const listKey = listOpen.value
    ? listKeys[event.key as keyof typeof listKeys]
    : undefined;

  if (listKey) {
    event.stopPropagation();
    listKey();

    return true;
  }

  if (event.key === 'Enter') {
    void submit();

    return true;
  }

  if (event.key === 'Escape' && replyTo.value) {
    event.stopPropagation();
    replyTo.value = null;

    return true;
  }

  return false;
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

// The editable box carries the combobox role and looks exactly like the shadcn Input; it grows with the text.
const boxClass = [
  'dark:bg-input/30 border-input focus-visible:border-ring focus-visible:ring-ring/50',
  'aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive',
  'dark:aria-invalid:border-destructive/50 aria-invalid:ring-3 focus-visible:ring-3',
  'max-h-32 min-h-9 w-full min-w-0 overflow-y-auto rounded-md border bg-transparent py-1 ps-2.5 pe-10 text-base',
  'leading-7 break-words whitespace-pre-wrap shadow-xs transition-[color,box-shadow] outline-none md:text-sm',
].join(' ');

const boxAttributes = computed<Record<string, string>>(() => {
  const always = {
    'data-composer': '',
    role: 'combobox',
    'aria-autocomplete': 'list',
    'aria-expanded': String(listOpen.value),
    'aria-label': t('dashboard.chat.composer.label'),
    'aria-placeholder': t('dashboard.chat.composer.placeholder'),
    'aria-invalid': String(tooLong.value),
    class: boxClass,
  };

  return listOpen.value
    ? {
        ...always,
        'aria-controls': listId,
        'aria-activedescendant': `${listId}-${list.active.value}`,
      }
    : always;
});

watchEffect(() => {
  editor.value?.setOptions({ editorProps: { attributes: boxAttributes.value } });
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

    editor.value?.commands.clearContent(true);
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
      <div data-composer-box class="relative min-w-0 flex-1">
        <EditorContent :editor="editor" />
        <span
          v-if="text === ''"
          aria-hidden="true"
          class="pointer-events-none absolute inset-y-0 inset-s-[calc(--spacing(2.5)+1px)] flex items-center text-base
            text-muted-foreground md:text-sm"
        >
          {{ $t('dashboard.chat.composer.placeholder') }}
        </span>
        <div class="absolute end-0.5 bottom-0.5">
          <EmotePicker
            :emotes="emotes.data.value ?? []"
            @pick="pickFromPicker"
            @closed="focusBox"
          />
        </div>
      </div>
      <span
        v-if="showCount"
        data-composer-count
        class="flex-none text-sm text-muted-foreground tabular-nums"
        :class="{ 'text-destructive': tooLong }"
      >
        {{ $t('dashboard.chat.composer.count', { count: text.length, max: chatMessageMaxLength }) }}
      </span>
      <Button type="submit" :disabled="!canSend">
        <Spinner v-if="sender.isPending.value" />
        {{ $t('dashboard.chat.composer.send') }}
      </Button>
    </div>
    <p v-if="problem" role="alert" class="text-sm text-destructive">{{ problem }}</p>
  </form>
</template>
