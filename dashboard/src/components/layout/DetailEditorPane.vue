<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The detail column of a list/detail page. It owns the phone Back link, the idle panel (nothing picked), the
// missing panel (the picked row is gone) and the column's size. The domain editor stays on its page, in the
// default slot; ListDetail owns whether the column shows at all.
import BackLink from '@/components/layout/BackLink.vue';
import EmptyState from '@/components/layout/EmptyState.vue';
import PanelCard from '@/components/layout/PanelCard.vue';

withDefaults(
  defineProps<{
    // A row is picked.
    open: boolean;
    // The picked row no longer exists.
    missing?: boolean;
    idleTitle: string;
    idleText: string;
    missingText?: string;
    backLabel: string;
  }>(),
  {
    missing: false,
    missingText: undefined,
  },
);

defineEmits<{ back: [] }>();
</script>

<template>
  <PanelCard
    v-if="!open"
    state="empty"
    :title="idleTitle"
  >
    <template #empty>
      <EmptyState kind="none" :description="idleText" />
    </template>
  </PanelCard>
  <div v-else class="flex flex-1 flex-col gap-2 md:min-h-0">
    <BackLink
      data-detail-back
      data-page-inset
      class="self-start md:hidden"
      :label="backLabel"
      @click="$emit('back')"
    />
    <slot name="header" />
    <PanelCard
      v-if="missing"
      state="empty"
      :title="idleTitle"
    >
      <template #empty>
        <EmptyState kind="noMatch" :description="missingText ?? idleText" />
      </template>
    </PanelCard>
    <slot v-else />
  </div>
</template>
