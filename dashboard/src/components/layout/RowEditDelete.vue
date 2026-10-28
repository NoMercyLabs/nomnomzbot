<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The row-end actions of a list whose rows edit in a dialog: Edit, then Delete, two 36 px icon buttons in
// the same order and size on every row (owner 2026-10-26: edit and delete have "a stable spot in the ui").
// Delete only asks: the page opens its ConfirmDialog from the delete event.
import Edit from '~icons/nnz/edit';
import Trash2 from '~icons/nnz/trash';

import IconAction from '@/components/layout/IconAction.vue';

defineProps<{ editLabel: string; deleteLabel: string }>();

defineEmits<{ edit: []; delete: [] }>();
</script>

<template>
  <span data-slot="row-edit-delete" class="flex flex-none items-center">
    <IconAction data-row-edit :label="editLabel" @click.stop="$emit('edit')">
      <Edit class="size-5" aria-hidden="true" />
    </IconAction>
    <IconAction data-row-delete :label="deleteLabel" @click.stop="$emit('delete')">
      <Trash2 class="size-5 text-destructive" aria-hidden="true" />
    </IconAction>
  </span>
</template>
