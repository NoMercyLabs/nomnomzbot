<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '@/lib/utils'
import { useTableStacked } from './stacked'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()

const stacked = useTableStacked()
</script>

<template>
  <tr
    data-slot="table-row"
    :class="cn(
      'hover:bg-muted/50 data-[state=selected]:bg-muted border-b transition-colors has-aria-expanded:bg-muted/50',
      stacked && 'max-md:flex max-md:flex-col max-md:gap-2 max-md:rounded-lg max-md:border max-md:p-3',
      props.class,
    )"
  >
    <slot />
  </tr>
</template>
