<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The leading mark of a connection row. A provider with a brand logo (PlatformLogo) shows it; every other
// provider shows a designer icon, so the 20px box is never empty and the names in a list start at one edge.
import { computed, type Component } from 'vue';
import ChatBot from '~icons/nnz/chat-bot';
import ChatMessages from '~icons/nnz/chat-messages';
import Coin from '~icons/nnz/coin';
import Headphone from '~icons/nnz/headphone';
import Heart from '~icons/nnz/heart';
import ShoppingBag from '~icons/nnz/shopping-bag';
import Webcam from '~icons/nnz/webcam';

import PlatformLogo from './PlatformLogo.vue';

const props = defineProps<{ provider: string }>();

// Only providers PlatformLogo has no logo for: twitch, kick, youtube and x keep their brand mark.
const icons: Readonly<Record<string, Component>> = {
  spotify: Headphone,
  discord: ChatMessages,
  patreon: Heart,
  shopify: ShoppingBag,
  treatstream: Coin,
  obs: Webcam,
  custom_bot: ChatBot,
};
const icon = computed(() => icons[props.provider]);
</script>

<template>
  <span data-slot="connection-logo" class="size-5 shrink-0">
    <component :is="icon"
               v-if="icon"
               class="size-5 text-muted-foreground"
               data-slot="provider-icon"
               aria-hidden="true"
    />
    <PlatformLogo v-else :platform="provider" class="size-5" />
  </span>
</template>
