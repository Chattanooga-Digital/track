<script setup lang="ts">
import { computed } from 'vue'
import type { AssignmentDto, CustomerDto } from '@/types/customer'

const props = defineProps<{
  assignment: AssignmentDto
  customers: Pick<CustomerDto, 'id' | 'name'>[]
  loading?: boolean
}>()

const emit = defineEmits<{ change: [assignment: AssignmentDto] }>()

const SHARED = 'shared'
const UNASSIGNED = 'unassigned'

const items = computed(() => [
  ...props.customers.map((c) => ({ title: c.name, value: `${c.id}` })),
  { title: 'Shared infrastructure', value: SHARED },
  { title: 'Unassigned', value: UNASSIGNED },
])

const selected = computed(() => {
  if (props.assignment.customerId !== null) return `${props.assignment.customerId}`
  return props.assignment.isShared ? SHARED : UNASSIGNED
})

function select(value: string) {
  if (value === selected.value) return
  emit(
    'change',
    value === SHARED || value === UNASSIGNED
      ? { customerId: null, isShared: value === SHARED }
      : { customerId: Number(value), isShared: false },
  )
}
</script>

<template>
  <v-select
    :model-value="selected"
    :items="items"
    :loading="loading"
    label="Assigned to"
    density="compact"
    variant="outlined"
    hide-details
    @update:model-value="select"
  />
</template>
