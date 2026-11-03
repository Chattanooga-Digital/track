<script setup lang="ts">
import { computed, ref, onMounted, watch } from 'vue'
import type { AssignmentDto, CustomerDto, StackDto } from '@/types/customer'
import type { HistoryBucketDto } from '@/types/stats'
import { getStacks, setStackAssignment } from '@/api/stacksApi'
import { getCustomers } from '@/api/customersApi'
import { getHistory } from '@/api/statsApi'
import MetricHistoryChart from '@/components/MetricHistoryChart.vue'
import AssignmentSelect from '@/components/AssignmentSelect.vue'

const stacks = ref<StackDto[]>([])
const customers = ref<CustomerDto[]>([])
const loading = ref(true)
const assigning = ref(false)

const selectedStack = ref<string | null>(null)
const hours = ref(24)
const history = ref<HistoryBucketDto[]>([])
const historyLoading = ref(false)

const hourOptions = [
  { title: 'Last 6 hours', value: 6 },
  { title: 'Last 24 hours', value: 24 },
  { title: 'Last 7 days', value: 168 },
  { title: 'Last 30 days', value: 720 },
]

const selectedStackDto = computed(() =>
  stacks.value.find((s) => s.stackName === selectedStack.value),
)

async function assign(assignment: AssignmentDto) {
  if (!selectedStack.value) return
  assigning.value = true
  try {
    await setStackAssignment(selectedStack.value, assignment)
    stacks.value = await getStacks()
  } finally {
    assigning.value = false
  }
}

async function refresh() {
  loading.value = true
  try {
    ;[stacks.value, customers.value] = await Promise.all([getStacks(), getCustomers()])
    if (!selectedStack.value && stacks.value[0]) {
      selectedStack.value = stacks.value[0].stackName
    }
  } finally {
    loading.value = false
  }
}

async function loadHistory() {
  if (!selectedStack.value) return
  historyLoading.value = true
  try {
    history.value = await getHistory({ stackName: selectedStack.value, hours: hours.value })
  } finally {
    historyLoading.value = false
  }
}

onMounted(refresh)
watch([selectedStack, hours], loadHistory)
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4 ga-4 flex-wrap">
      <h1 class="text-h5">Stacks</h1>
      <v-spacer />
      <v-select
        v-model="selectedStack"
        :items="stacks.map((s) => s.stackName)"
        label="Stack"
        density="compact"
        hide-details
        style="max-width: 260px"
      />
      <v-select
        v-model="hours"
        :items="hourOptions"
        label="Range"
        density="compact"
        hide-details
        style="max-width: 200px"
      />
    </div>

    <v-progress-linear v-if="loading" indeterminate />

    <v-row>
      <v-col cols="12" md="4">
        <v-card variant="outlined">
          <v-list density="compact" nav>
            <v-list-subheader>All stacks</v-list-subheader>
            <v-list-item
              v-for="stack in stacks"
              :key="stack.stackName"
              :active="stack.stackName === selectedStack"
              @click="selectedStack = stack.stackName"
            >
              <template #prepend>
                <v-icon
                  :icon="stack.isActive ? 'mdi-layers' : 'mdi-layers-off'"
                  :color="stack.isActive ? 'success' : undefined"
                />
              </template>
              <v-list-item-title>{{ stack.stackName }}</v-list-item-title>
              <v-list-item-subtitle>
                {{
                  stack.customerName ?? (stack.isShared ? 'Shared infrastructure' : 'Unassigned')
                }}
                · {{ stack.containerCount }} running
              </v-list-item-subtitle>
            </v-list-item>
          </v-list>
        </v-card>
      </v-col>

      <v-col cols="12" md="8">
        <v-card v-if="selectedStackDto" variant="outlined" class="mb-4">
          <div class="d-flex flex-wrap align-center ga-4 pa-4">
            <div class="flex-grow-1">
              <div class="text-subtitle-1 font-weight-medium">{{ selectedStackDto.stackName }}</div>
              <div class="text-body-2 text-medium-emphasis">
                {{ selectedStackDto.containerCount }} running
              </div>
            </div>
            <AssignmentSelect
              :assignment="selectedStackDto"
              :customers="customers"
              :loading="assigning"
              class="assignment-select"
              @change="assign"
            />
          </div>
        </v-card>
        <v-card v-if="selectedStack" variant="outlined" class="mb-4">
          <v-card-title class="text-subtitle-1">CPU</v-card-title>
          <v-card-text>
            <MetricHistoryChart :buckets="history" metric="cpu" />
          </v-card-text>
        </v-card>
        <v-card v-if="selectedStack" variant="outlined" class="mb-4">
          <v-card-title class="text-subtitle-1">Memory</v-card-title>
          <v-card-text>
            <MetricHistoryChart :buckets="history" metric="memory" />
          </v-card-text>
        </v-card>
        <v-card v-if="selectedStack" variant="outlined">
          <v-card-title class="text-subtitle-1">Disk</v-card-title>
          <v-card-text>
            <MetricHistoryChart :buckets="history" metric="disk" />
          </v-card-text>
        </v-card>
        <v-progress-linear v-if="historyLoading" indeterminate class="mt-2" />
      </v-col>
    </v-row>
  </div>
</template>

<style scoped>
.assignment-select {
  flex: 0 1 260px;
}
</style>
