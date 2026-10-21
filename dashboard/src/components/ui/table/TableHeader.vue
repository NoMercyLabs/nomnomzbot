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
  <thead
    data-slot="table-header"
    :class="cn('[&_tr]:border-b', stacked && 'max-md:hidden', props.class)"
  >
    <slot />
  </thead>
</template>
