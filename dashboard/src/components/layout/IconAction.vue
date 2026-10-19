<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
