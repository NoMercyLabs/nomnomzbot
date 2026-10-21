<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A small icon-only button that copies a web address and shows a check for a moment. It sits right beside the
// address, on the same line.
import { refAutoReset } from '@vueuse/core';
import CheckCircle from '~icons/nnz/check-circle';
import Link from '~icons/nnz/link';

import { Button } from '@/components/ui/button';

const props = defineProps<{ url: string }>();

const copied = refAutoReset(false, 2000);

async function copy(): Promise<void> {
  await navigator.clipboard.writeText(props.url);
  copied.value = true;
}
</script>

<template>
  <Button
    data-copy-link
    variant="ghost"
    size="icon"
    class="-my-1.5 align-middle"
    :aria-label="copied ? $t('common.copied') : $t('site.setup.copyLink')"
    @click="copy"
  >
    <CheckCircle v-if="copied" class="size-5" aria-hidden="true" />
    <Link v-else class="size-5" aria-hidden="true" />
  </Button>
</template>
