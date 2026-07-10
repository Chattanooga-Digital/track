<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import type { CustomerDto } from '@/types/customer'
import type { StackDto } from '@/types/customer'
import {
  getCustomers,
  createCustomer,
  updateCustomer,
  deleteCustomer,
  setCustomerStacks,
} from '@/api/customersApi'
import { getStacks } from '@/api/stacksApi'

const customers = ref<CustomerDto[]>([])
const stacks = ref<StackDto[]>([])
const loading = ref(true)

const editDialog = ref(false)
const editing = ref<CustomerDto | null>(null)
const form = ref({ name: '', notes: '' })

const stacksDialog = ref(false)
const stacksTarget = ref<CustomerDto | null>(null)
const selectedStacks = ref<string[]>([])

async function refresh() {
  loading.value = true
  try {
    ;[customers.value, stacks.value] = await Promise.all([getCustomers(), getStacks()])
  } finally {
    loading.value = false
  }
}

onMounted(refresh)

// Stacks owned by another customer are shown but flagged, since assigning moves them here.
const stackOptions = computed(() =>
  stacks.value.map(s => ({
    title: s.customerName && s.customerId !== stacksTarget.value?.id
      ? `${s.stackName} (currently ${s.customerName})`
      : s.stackName,
    value: s.stackName,
  })),
)

function openCreate() {
  editing.value = null
  form.value = { name: '', notes: '' }
  editDialog.value = true
}

function openEdit(customer: CustomerDto) {
  editing.value = customer
  form.value = { name: customer.name, notes: customer.notes ?? '' }
  editDialog.value = true
}

async function saveCustomer() {
  const model = { name: form.value.name, notes: form.value.notes || null }
  if (editing.value) {
    await updateCustomer(editing.value.id, model)
  } else {
    await createCustomer(model)
  }
  editDialog.value = false
  await refresh()
}

async function removeCustomer(customer: CustomerDto) {
  if (!confirm(`Delete customer "${customer.name}"? Its stack assignments will be released.`)) return
  await deleteCustomer(customer.id)
  await refresh()
}

function openStacks(customer: CustomerDto) {
  stacksTarget.value = customer
  selectedStacks.value = [...customer.stackNames]
  stacksDialog.value = true
}

async function saveStacks() {
  if (!stacksTarget.value) return
  await setCustomerStacks(stacksTarget.value.id, selectedStacks.value)
  stacksDialog.value = false
  await refresh()
}
</script>

<template>
  <v-container fluid>
    <div class="d-flex align-center mb-4">
      <h1 class="text-h5">Customers</h1>
      <v-spacer />
      <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New Customer</v-btn>
    </div>

    <v-progress-linear v-if="loading" indeterminate />

    <v-card variant="outlined">
      <v-table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Stacks</th>
            <th>Notes</th>
            <th class="text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="customer in customers" :key="customer.id">
            <td class="font-weight-medium">{{ customer.name }}</td>
            <td>
              <v-chip v-for="name in customer.stackNames" :key="name" size="x-small" class="ma-1" variant="tonal">
                {{ name }}
              </v-chip>
              <span v-if="customer.stackNames.length === 0" class="text-medium-emphasis">None</span>
            </td>
            <td class="text-medium-emphasis">{{ customer.notes }}</td>
            <td class="text-right text-no-wrap">
              <v-btn size="small" variant="text" icon="mdi-layers-edit" @click="openStacks(customer)" />
              <v-btn size="small" variant="text" icon="mdi-pencil" @click="openEdit(customer)" />
              <v-btn size="small" variant="text" icon="mdi-delete" color="error" @click="removeCustomer(customer)" />
            </td>
          </tr>
          <tr v-if="!loading && customers.length === 0">
            <td colspan="4" class="text-center text-medium-emphasis py-6">No customers yet.</td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <v-dialog v-model="editDialog" max-width="480">
      <v-card>
        <v-card-title>{{ editing ? 'Edit Customer' : 'New Customer' }}</v-card-title>
        <v-card-text>
          <v-text-field v-model="form.name" label="Name" autofocus />
          <v-textarea v-model="form.notes" label="Notes" rows="2" />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="editDialog = false">Cancel</v-btn>
          <v-btn color="primary" :disabled="!form.name.trim()" @click="saveCustomer">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-dialog v-model="stacksDialog" max-width="520">
      <v-card>
        <v-card-title>Assign stacks to {{ stacksTarget?.name }}</v-card-title>
        <v-card-text>
          <v-select
            v-model="selectedStacks"
            :items="stackOptions"
            label="Stacks"
            multiple
            chips
            closable-chips
          />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="stacksDialog = false">Cancel</v-btn>
          <v-btn color="primary" @click="saveStacks">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </v-container>
</template>
