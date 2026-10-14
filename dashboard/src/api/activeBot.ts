// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useLocalStorage } from '@vueuse/core';

import { servedOrigin, toBotOrigin } from '@/lib/botAddress';

// The bot this client talks to: the one serving the page, a home-network bot or the hosted one.
// Change it through switchBot in auth.ts, which also drops the old bot's sign-in and answers.
export const activeBot = useLocalStorage<string>('nnz.bot', servedOrigin, {
  serializer: { read: (raw) => toBotOrigin(raw) ?? servedOrigin, write: (origin) => origin },
});
