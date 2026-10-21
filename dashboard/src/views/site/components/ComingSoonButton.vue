<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
