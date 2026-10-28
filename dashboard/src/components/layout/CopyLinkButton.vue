<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Copies a link and says so for a moment, like the KMP app's CopyLinkButton (owner 2026-10-14: "make a copy
// link possible like the old kmp has").
import { refAutoReset } from '@vueuse/core';
import CheckCircle from '~icons/nnz/check-circle';
import Link from '~icons/nnz/link';

import { Button } from '@/components/ui/button';

const props = defineProps<{
  url: string;
  label: string;
}>();

const copied = refAutoReset(false, 2000);

async function copy(): Promise<void> {
  await navigator.clipboard.writeText(props.url);
  copied.value = true;
}
</script>

<template>
  <Button data-copy-link variant="outline" @click="copy">
    <CheckCircle v-if="copied" class="size-5" aria-hidden="true" />
    <Link v-else class="size-5" aria-hidden="true" />
    {{ copied ? $t('common.copied') : label }}
  </Button>
</template>
