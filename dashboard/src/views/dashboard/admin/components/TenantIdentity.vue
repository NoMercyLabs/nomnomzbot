<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The person of a tenant row: the avatar, the display name and the login. The row wraps it in the one link
// (a streamer's console) or the one button (a viewer's detail).
import { Avatar, AvatarFallback } from '@/components/ui/avatar';

import { initialsOf } from './channelDisplay';

defineProps<{ displayName: string; login: string }>();
</script>

<template>
  <Avatar class="size-8 rounded-lg">
    <AvatarFallback class="rounded-lg">{{ initialsOf(displayName) }}</AvatarFallback>
  </Avatar>
  <span class="flex min-w-0 flex-col">
    <span class="truncate font-medium">{{ displayName }}</span>
    <span class="truncate text-sm text-muted-foreground">{{ login }}</span>
  </span>
</template>
