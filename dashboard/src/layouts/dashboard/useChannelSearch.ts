// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { refDebounced } from '@vueuse/core';
import { computed } from 'vue';

import { useChannelDirectory } from '@/api/channels';
import { useCommand } from '@/components/ui/command';

const SEARCH_DELAY_MS = 250;

// The directory search of the channel pickers, read from the search box of the Command it sits in. Every channel
// the bot serves is asked from the server; `pending` is true from the first key until that answer is in, so a
// picker says "Searching" there instead of "No channel found" about the old, shorter list.
export function useChannelSearch() {
  const { filterState } = useCommand();
  const typed = computed(() => filterState.search.trim());
  const asked = refDebounced(typed, SEARCH_DELAY_MS);
  const directory = useChannelDirectory(asked);
  const pending = computed(() =>
    typed.value !== asked.value || directory.isFetching.value);

  return {
    directory,
    pending,
  };
}
