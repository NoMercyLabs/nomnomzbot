// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component, InjectionKey } from 'vue';

// The frame's own controls (Shield mode, the error inbox) end the page header's title row,
// so a page has one header, as in the mockups (frame.css:60).
export const frameActionsKey: InjectionKey<Component> = Symbol('frameActions');
