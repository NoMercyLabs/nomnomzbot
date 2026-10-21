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
  <tbody
    data-slot="table-body"
    :class="cn('[&_tr:last-child]:border-0', stacked && 'max-md:flex max-md:flex-col max-md:gap-3 max-md:[&_tr:last-child]:border', props.class)"
  >
    <slot />
  </tbody>
</template>
