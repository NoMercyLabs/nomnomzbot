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

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import AppMark from '@/components/layout/AppMark.vue';
import botIcon from '@/assets/icons/line/chat-bot.svg?raw';

describe('the app mark', () => {
  it('draws the designer chat-bot glyph in the text color, not the color baked into the file', () => {
    const mark = mount(AppMark);
    const html = mark.html();

    const firstPath = /<path d="([^"]+)"/.exec(botIcon)?.[1] ?? '';

    expect(firstPath).not.toBe('');
    expect(html).toContain(firstPath);
    expect(html).toContain('stroke="currentColor"');
    expect(html).not.toContain('#11181C');
  });
});
