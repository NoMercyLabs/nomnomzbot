<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// One pick list as a row: its name, how many entries it holds and the helper that draws from it, then Edit and
// Delete, each behind its own permission.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PickList } from '@/api/pickLists';
import RowEditDelete from '@/components/layout/RowEditDelete.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCan } from '@/composables/useCan';

const props = defineProps<{ list: PickList }>();

defineEmits<{ edit: []; remove: [] }>();

const { t } = useI18n();
const { can } = useCan();

const detail = computed<string>(() =>
  [
    t('dashboard.picklists.entries', props.list.items.length),
    `{list.pick.${props.list.name}}`,
    props.list.description,
  ]
    .filter(Boolean)
    .join(' · '));

const showActions = computed<boolean>(() => can('picklists:write') || can('picklists:delete'));
</script>

<template>
  <SummaryRow :title="list.name" :detail="detail">
    <template v-if="showActions" #trailing>
      <RowEditDelete
        :edit-label="t('dashboard.picklists.edit', { name: list.name })"
        :delete-label="t('dashboard.picklists.delete.title', { name: list.name })"
        :editable="can('picklists:write')"
        :deletable="can('picklists:delete')"
        @edit="$emit('edit')"
        @delete="$emit('remove')"
      />
    </template>
  </SummaryRow>
</template>
