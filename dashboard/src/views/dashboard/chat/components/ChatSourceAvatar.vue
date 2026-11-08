<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel a shared-chat line came from, as a small avatar. The channel name is its tooltip and accessible label.
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';

defineProps<{
  name: string;
  avatarUrl?: string | null;
}>();
</script>

<template>
  <Tooltip>
    <TooltipTrigger as-child>
      <Avatar
        data-source-channel
        role="img"
        :aria-label="name"
        class="size-4 flex-none"
      >
        <AvatarImage v-if="avatarUrl" :src="avatarUrl" alt="" />
        <AvatarFallback class="bg-muted text-[0.625rem] text-muted-foreground">{{ name.charAt(0).toUpperCase() }}</AvatarFallback>
      </Avatar>
    </TooltipTrigger>
    <TooltipContent>{{ name }}</TooltipContent>
  </Tooltip>
</template>
