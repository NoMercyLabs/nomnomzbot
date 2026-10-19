<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The foot of a capped list panel (the PanelCard footer slot): one ghost button that reveals the next rows.
import { Button } from '@/components/ui/button';

import ChevronDown from '~icons/nnz/chevron-down';

defineProps<{ remaining: number }>();
defineEmits<{ more: [] }>();
</script>

<template>
  <Button
    data-slot="show-more"
    type="button"
    variant="ghost"
    size="sm"
    class="w-full"
    @click="$emit('more')"
  >
    {{ $t("common.showMore", { count: remaining }) }}
    <ChevronDown aria-hidden="true" />
  </Button>
</template>
