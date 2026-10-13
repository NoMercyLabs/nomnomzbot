// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
