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
    // Squeezed in a header row it shrank to 76px and its words ran out past the edge.
    expect(toggle).toContain('shrink-0');
  });
});
