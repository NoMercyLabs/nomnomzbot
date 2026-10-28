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
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';

import Edit from '~icons/nnz/edit';
import Trash from '~icons/nnz/trash';

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
      <Button
        v-if="can('picklists:write')"
        variant="ghost"
        size="icon"
        data-picklist-edit
        :aria-label="t('dashboard.picklists.edit', { name: list.name })"
        :title="t('dashboard.picklists.edit', { name: list.name })"
        @click="$emit('edit')"
      >
        <Edit class="size-5" aria-hidden="true" />
      </Button>
      <Button
        v-if="can('picklists:delete')"
        variant="ghost"
        size="icon"
        data-picklist-delete
        :aria-label="t('dashboard.picklists.delete.title', { name: list.name })"
        :title="t('dashboard.picklists.delete.title', { name: list.name })"
        @click="$emit('remove')"
      >
        <Trash class="size-5" aria-hidden="true" />
      </Button>
    </template>
  </SummaryRow>
</template>
