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

import { useLocalStorage } from '@vueuse/core';

import { servedOrigin, toBotOrigin } from '@/lib/botAddress';

// The bot this client talks to: the one serving the page, a home-network bot or the hosted one.
// Change it through switchBot in auth.ts, which also drops the old bot's sign-in and answers.
export const activeBot = useLocalStorage<string>('nnz.bot', servedOrigin, {
  serializer: { read: (raw) => toBotOrigin(raw) ?? servedOrigin, write: (origin) => origin },
});
