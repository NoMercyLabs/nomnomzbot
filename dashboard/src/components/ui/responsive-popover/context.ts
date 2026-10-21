// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { InjectionKey, Ref } from 'vue';

// Whether the surrounding ResponsivePopover is a bottom sheet (a narrow window) or a popover.
export const sheetKey: InjectionKey<Ref<boolean>> = Symbol('responsive-popover-sheet');
