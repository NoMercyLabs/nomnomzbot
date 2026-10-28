<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// An icon-only row action: a ghost icon button (36 px) that names itself in a tooltip on hover and keyboard
// focus, and to a screen reader through aria-label. The icon goes in the slot (class size-5).
// The frame mounts the one TooltipProvider; a row never stacks its own.
import { Button } from '@/components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';

defineOptions({ inheritAttrs: false });

defineProps<{ label: string }>();
</script>

<template>
  <Tooltip>
    <TooltipTrigger as-child>
      <Button v-bind="$attrs"
              type="button"
              variant="ghost"
              size="icon"
              :aria-label="label"
      >
        <slot />
      </Button>
    </TooltipTrigger>
    <TooltipContent>{{ label }}</TooltipContent>
  </Tooltip>
</template>
