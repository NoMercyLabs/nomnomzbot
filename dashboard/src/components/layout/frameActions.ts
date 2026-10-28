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

import type { Component, InjectionKey } from 'vue';

// The frame's own controls (Shield mode, the error inbox) end the page header's title row,
// so a page has one header, as in the mockups (frame.css:60).
export const frameActionsKey: InjectionKey<Component> = Symbol('frameActions');
