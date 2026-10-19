<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The close button of an open command's panel. A phone has the "Back to the list" link instead.
import Cross from '~icons/nnz/cross';

import IconAction from '@/components/layout/IconAction.vue';
</script>

<template>
  <IconAction data-detail-close class="max-md:hidden" :label="$t('common.close')">
    <Cross class="size-5" aria-hidden="true" />
  </IconAction>
</template>
