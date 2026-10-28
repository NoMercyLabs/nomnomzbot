<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The sign-in action while sign-in is closed: a disabled button with its label, and a short note.
// The note is always read to a screen reader; it shows on screen only where there is room (showNote).
import { useId } from 'vue';

import { Button } from '@/components/ui/button';

type Variant = InstanceType<typeof Button>['$props']['variant'];
type Size = InstanceType<typeof Button>['$props']['size'];

withDefaults(
  defineProps<{
    label: string;
    note: string;
    showNote?: boolean;
    variant?: Variant;
    size?: Size;
  }>(),
  {
    showNote: false,
    variant: 'default',
    size: 'default',
  },
);

const noteId = useId();
</script>

<template>
  <div class="flex flex-col items-center gap-2">
    <Button disabled :variant="variant" :size="size" :aria-describedby="noteId">{{ label }}</Button>
    <p :id="noteId" class="text-sm text-muted-foreground" :class="showNote ? '' : 'sr-only'">
      {{ note }}
    </p>
  </div>
</template>
