<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one wait line of a public task (sign in, accept an invite, first-run setup): the spinner beside what the
// page is waiting for, announced once as a status. A long step never reads as a frozen page.
import { Spinner } from '@/components/ui/spinner';
</script>

<template>
  <p role="status" class="flex items-center gap-2 text-sm text-muted-foreground">
    <Spinner role="presentation" aria-hidden="true" class="flex-none" />
    <span><slot /></span>
  </p>
</template>
