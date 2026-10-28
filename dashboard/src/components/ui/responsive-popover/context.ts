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

import type { InjectionKey, Ref } from 'vue';

// Whether the surrounding ResponsivePopover is a bottom sheet (a narrow window) or a popover.
export const sheetKey: InjectionKey<Ref<boolean>> = Symbol('responsive-popover-sheet');
