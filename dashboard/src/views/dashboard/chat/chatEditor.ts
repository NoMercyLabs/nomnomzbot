// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { Extension, Node, type JSONContent } from '@tiptap/core';
import { Plugin } from '@tiptap/pm/state';
import type { EditorView } from '@tiptap/pm/view';

// The send box shows emotes as images while you type, as Twitch and 7TV do; the message that leaves is plain
// text with the emote codes. Nothing outside the box can become an image in it: a paste or a drop goes in as
// text, and only an emote from your own list turns into one.
export type EmoteImage = { code: string; src: string };

export type EmoteLookup = (word: string) => EmoteImage | undefined;

export const EmoteNode = Node.create({
  name: 'emote',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: false,
  draggable: false,

  addAttributes() {
    return {
      code: { default: '' },
      src: { default: '' },
    };
  },

  // Pasted HTML never becomes an emote.
  parseHTML() {
    return [];
  },

  renderHTML({ node }) {
    return ['img', {
      src: node.attrs.src,
      alt: node.attrs.code,
      title: node.attrs.code,
      'data-emote': node.attrs.code,
      draggable: 'false',
      class: 'inline-block h-7 max-w-none align-middle',
    }];
  },

  renderText({ node }) {
    return node.attrs.code as string;
  },
});

// Text split into words, each known emote code as an emote; line breaks become spaces (chat is one line).
export function emotified(text: string, lookup: EmoteLookup): JSONContent[] {
  return text.replace(/\s*\n\s*/gu, ' ').split(/(\s+)/u).filter((part) =>
    part !== '').map((part) => {
    const emote = lookup(part);

    return emote
      ? { type: 'emote', attrs: emote }
      : { type: 'text', text: part };
  });
}

export type ChatInputOptions = {
  lookup: EmoteLookup;
  onKey: (event: KeyboardEvent) => boolean;
};

// The word just before the cursor turns into its emote when a space follows it.
function emoteOnSpace(
  view: EditorView,
  range: {
    from: number;
    to: number;
    text: string;
  },
  lookup: EmoteLookup,
): boolean {
  const { from, to, text } = range;

  if (text !== ' ')
    return false;

  const $from = view.state.doc.resolve(from);
  const before = $from.parent.textBetween(0, $from.parentOffset, undefined, '￼');
  const word = /(\S+)$/u.exec(before)?.[1];
  const emote = word
    ? lookup(word)
    : undefined;

  if (!word || !emote)
    return false;

  const { schema } = view.state;
  const image = schema.nodes.emote!.create(emote);
  const swap = view.state.tr.replaceWith(from - word.length, to, [image, schema.text(' ')]);

  view.dispatch(swap);

  return true;
}

export const ChatInput = Extension.create<ChatInputOptions>({
  name: 'chatInput',

  addOptions() {
    return {
      lookup: () =>
        undefined,
      onKey: () =>
        false,
    };
  },

  addProseMirrorPlugins() {
    const { editor, options } = this;

    return [new Plugin({
      props: {
        handleTextInput: (view, from, to, text) =>
          emoteOnSpace(view, {
            from,
            to,
            text,
          }, options.lookup),
        handleKeyDown: (_view, event) =>
          options.onKey(event),
        handlePaste: (_view, event) => {
          const text = event.clipboardData?.getData('text/plain') ?? '';

          editor.commands.insertContent(emotified(text, options.lookup));

          return true;
        },
        handleDrop: () =>
          true,
      },
    })];
  },
});
