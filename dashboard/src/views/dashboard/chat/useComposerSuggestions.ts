// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, ref, shallowRef, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue';

import type { ChatEmoteEntry } from '@/api/chat';
import { merge } from '@/lib/array';

import {
  chatterSuggestions,
  emojiSuggestions,
  emoteSuggestions,
  suggestionLimit,
  tokenAt,
  type Suggestion,
} from './composerSuggest';
import { loadEmoji, type EmojiEntry } from './emojiCatalog';

// The list the composer offers for the word at the caret: emotes then emoji after ":", chatters after "@".
// Escape hides it until the text changes; each new word starts at the top of the list.
export function useComposerSuggestions(
  text: Ref<string>,
  caret: Ref<number>,
  emotes: MaybeRefOrGetter<readonly ChatEmoteEntry[]>,
  chatters: MaybeRefOrGetter<readonly string[]>,
) {
  const emoji = shallowRef<EmojiEntry[]>([]);
  const dismissed = ref(false);
  const active = ref(0);
  const token = computed(() => tokenAt(text.value, caret.value));

  async function fillEmoji(): Promise<void> {
    emoji.value = await loadEmoji();
  }

  watch(token, (next) => {
    active.value = 0;

    if (next?.trigger === ':' && emoji.value.length === 0) {
      void fillEmoji();
    }
  });

  watch(text, () => {
    dismissed.value = false;
  });

  const suggestions = computed<Suggestion[]>(() => {
    const found = token.value;

    if (!found || dismissed.value)
      return [];

    if (found.trigger === '@')
      return chatterSuggestions(toValue(chatters), found.query);

    const emoteHits = emoteSuggestions(toValue(emotes), found.query);
    const emojiHits = emojiSuggestions(emoji.value, found.query);

    return merge(emoteHits, emojiHits).slice(0, suggestionLimit);
  });

  function move(step: number): void {
    const count = suggestions.value.length;

    active.value = (active.value + step + count) % count;
  }

  function dismiss(): void {
    dismissed.value = true;
  }

  return {
    token,
    suggestions,
    active,
    move,
    dismiss,
  };
}
