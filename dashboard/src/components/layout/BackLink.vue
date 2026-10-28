<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one back link (owner 2026-10-18: "a back button does not belong on the right side on a LTR page"). It
// sits at the start of its container (left in LTR, right in RTL), so the arrow flips with the direction.
// With `to` it navigates to that route location; without it, it only emits click and the page decides.
import type { RouteLocationRaw } from 'vue-router';
import { useRouter } from 'vue-router';

import { Button } from '@/components/ui/button';
import ArrowLeft from '~icons/nnz/arrow-left';

const props = defineProps<{ to?: RouteLocationRaw; label?: string }>();
const emit = defineEmits<{ click: [] }>();

const router = useRouter();

function go(): void {
  emit('click');

  if (props.to) {
    void router.push(props.to);
  }
}
</script>

<template>
  <Button data-slot="back-link"
          type="button"
          variant="ghost"
          @click="go"
  >
    <ArrowLeft aria-hidden="true" class="rtl:rotate-180" />
    {{ label ?? $t('common.back') }}
  </Button>
</template>
