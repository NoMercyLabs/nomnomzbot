<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The hint of a form field: a small info icon after the label that shows the hint on hover, on keyboard
// focus and on click or tap (touch has no hover). Replaces the gray FieldDescription line under a field.
// The 36px touch target overlaps the row instead of growing it, so a label with a hint lines up with one without.
import { ref } from 'vue';
import InfoCircle from '~icons/nnz/info-circle';

import { Button } from '@/components/ui/button';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';

const open = ref(false);
</script>

<template>
  <TooltipProvider :delay-duration="150">
  <Tooltip :open="open" @update:open="open = $event">
    <TooltipTrigger as-child>
      <Button
        data-slot="field-info"
        type="button"
        variant="ghost"
        size="icon"
        class="text-muted-foreground hover:text-foreground size-9! -my-3 shrink-0 p-0!"
        :aria-label="$t('common.fieldInfo')"
        @click.prevent="open = !open"
      >
        <InfoCircle class="size-5" aria-hidden="true" />
        <span class="sr-only"><slot /></span>
      </Button>
    </TooltipTrigger>
    <TooltipContent class="max-w-xs text-pretty">
      <slot />
    </TooltipContent>
  </Tooltip>
  </TooltipProvider>
</template>
