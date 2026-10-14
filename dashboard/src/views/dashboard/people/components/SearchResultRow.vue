<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One hit of the all-users search: avatar and name only, so typing stays quick.
// A person this channel has never seen says so in plain text with a dot.
// The marker shares the detail line, so the name keeps the full width and never breaks.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ViewerOption } from '@/api/community';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';

const props = defineProps<{ option: ViewerOption; selected: boolean }>();

defineEmits<{ pick: [] }>();

const { t } = useI18n();

const initials = computed<string>(() =>
  props.option.label.slice(0, 2).toUpperCase(),
);

const detail = computed<string>(() =>
  props.option.inChannel
    ? props.option.subLabel
    : `${props.option.subLabel} · ${t('dashboard.people.list.notInChannel')}`,
);
</script>

<template>
  <div data-search-result class="px-1 py-0.5">
    <SummaryRow
      pick
      :selected="selected"
      :aria-pressed="selected"
      :title="option.label"
      :detail="detail"
      @click="$emit('pick')"
    >
      <template #leading>
        <Avatar class="size-9">
          <AvatarImage v-if="option.imageUrl" :src="option.imageUrl" :alt="option.label" />
          <AvatarFallback>{{ initials }}</AvatarFallback>
        </Avatar>
      </template>
    </SummaryRow>
  </div>
</template>
