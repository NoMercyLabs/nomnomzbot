// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';

import { buttonVariants } from '@/components/ui/button';
import { toggleVariants } from '@/components/ui/toggle';

// The Shield toggle sits beside kit outline buttons in the page header; a grey hairline pill beside them
// read as broken (owner screenshot 2026-10-14).
describe('outline toggle', () => {
  it('wears the kit outline button look: filled surface, 12px corners, no grey hairline', () => {
    const toggle = toggleVariants({ variant: 'outline', size: 'sm' }).split(' ');
    const button = buttonVariants({ variant: 'outline' }).split(' ');

    for (const look of ['rounded-[12px]', 'bg-(--kit-surface)', 'text-(--kit-action-text)']) {
      expect(button).toContain(look);
      expect(toggle).toContain(look);
    }

    expect(toggle).not.toContain('border-input');
  });
});
