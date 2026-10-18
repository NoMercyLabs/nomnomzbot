<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The filters on one open source, each with an on/off switch.
import { toastObsError } from '../obsToast';

import { useObsFilterEnabled, useObsSourceFilters, type ObsFilter } from '@/api/obsSources';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ sourceName: string }>();

const { channelId } = useCurrentChannel();
const filters = useObsSourceFilters(channelId, () => props.sourceName);
const setEnabled = useObsFilterEnabled(channelId);

function toggle(filter: ObsFilter, enabled: boolean): void {
  setEnabled.mutate({
    sourceName: props.sourceName,
    filterName: filter.name,
    enabled,
  }, { onError: toastObsError });
}
</script>

<template>
  <div data-source-filters class="mb-1 ml-4 border-l pl-3 text-sm">
    <p v-if="filters.isPending.value" class="py-1.5 text-muted-foreground">
      {{ $t('dashboard.obs.sourceItems.filtersLoading') }}
    </p>
    <p v-else-if="filters.isError.value" class="py-1.5 text-destructive">
      {{ $t('dashboard.obs.sourceItems.filtersError') }}
    </p>
    <p v-else-if="filters.data.value?.length === 0" class="py-1.5 text-muted-foreground">
      {{ $t('dashboard.obs.sourceItems.noFilters') }}
    </p>
    <label
      v-for="filter in filters.data.value"
      v-else
      :key="filter.name"
      data-source-filter
      class="flex items-center justify-between gap-3 py-1.5"
    >
      <span class="min-w-0 truncate">{{ filter.name }}</span>
      <Switch
        :model-value="filter.enabled"
        :aria-label="$t('dashboard.obs.sourceItems.filterEnabled', { name: filter.name })"
        data-source-filter-enabled
        @update:model-value="(value) => toggle(filter, value === true)"
      />
    </label>
  </div>
</template>
