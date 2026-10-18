<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One feed: its name and the `custom.<name>` event it makes, how its data arrives, when data last came, the
// fields that failed on the last datum, and the on/off switch. A push feed also shows the link the source posts to.
import Edit from '~icons/nnz/edit';
import Lightning from '~icons/nnz/lightning';
import Trash from '~icons/nnz/trash';
import { computed } from 'vue';

import type { EventFeed } from '@/api/customEventFeeds';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import EnabledState from '@/components/layout/EnabledState.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';

const { can } = useCan();
const props = defineProps<{ feed: EventFeed; busy: boolean }>();

defineEmits<{
  toggle: [];
  test: [];
  edit: [];
  remove: [];
}>();

const eventName = computed(() => `custom.${props.feed.name}`);

const failedFields = computed(() => Object.keys(props.feed.fieldErrors));

const lastReceived = computed(() =>
  props.feed.lastReceivedAt
    ? new Date(props.feed.lastReceivedAt).toLocaleString()
    : undefined);
</script>

<template>
  <div
    :data-feed="feed.name"
    class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
  >
    <div class="min-w-0 flex-1">
      <span class="flex flex-wrap items-center gap-2">
        <span :id="`feed-${feed.name}`" class="text-sm font-medium wrap-break-word">{{ feed.displayName }}</span>
        <Badge variant="outline">{{ $t(`dashboard.settings.customEventFeeds.kind.${feed.sourceKind}`) }}</Badge>
      </span>
      <span class="block font-mono text-sm text-muted-foreground">{{ eventName }}</span>
      <span data-last-received class="block text-sm text-muted-foreground">
        {{
          lastReceived
            ? $t('dashboard.settings.customEventFeeds.lastReceived', { when: lastReceived })
            : $t('dashboard.settings.customEventFeeds.neverReceived')
        }}
      </span>
      <span v-if="failedFields.length > 0" data-field-errors class="block text-sm text-destructive">
        {{ $t('dashboard.settings.customEventFeeds.fieldErrors', { fields: failedFields.join(', ') }) }}
      </span>
    </div>
    <CopyLinkButton
      v-if="feed.inboundUrl"
      :url="feed.inboundUrl"
      :label="$t('dashboard.settings.customEventFeeds.copyLink')"
    />
    <div v-if="can('customdata:write')" class="flex items-center gap-1">
      <Button
        variant="ghost"
        size="icon"
        data-feed-test
        :aria-label="$t('dashboard.settings.customEventFeeds.test.open', { name: feed.displayName })"
        @click="$emit('test')"
      >
        <Lightning class="size-5" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        data-feed-edit
        :aria-label="$t('dashboard.settings.customEventFeeds.edit.open', { name: feed.displayName })"
        @click="$emit('edit')"
      >
        <Edit class="size-5" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        data-feed-delete
        :aria-label="$t('dashboard.settings.customEventFeeds.delete.open', { name: feed.displayName })"
        @click="$emit('remove')"
      >
        <Trash class="size-5" />
      </Button>
    </div>
    <Switch
      v-if="can('customdata:write')"
      :model-value="feed.isEnabled"
      :disabled="busy"
      :aria-labelledby="`feed-${feed.name}`"
      @update:model-value="$emit('toggle')"
    />
    <EnabledState v-else :enabled="feed.isEnabled" />
  </div>
</template>
