<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import type { BucketDto, BucketRefDto, CustomerDto } from '@/types/customer'
import type { StackDto } from '@/types/customer'
import {
  getCustomers,
  createCustomer,
  updateCustomer,
  deleteCustomer,
  setCustomerStacks,
  setCustomerBuckets,
} from '@/api/customersApi'
import { getStacks, setSharedStacks } from '@/api/stacksApi'
import { getBuckets, setSharedBuckets } from '@/api/bucketsApi'
import { apiErrorMessage } from '@/api/http'
import ConfirmDialog from '@/components/ConfirmDialog.vue'

const customers = ref<CustomerDto[]>([])
const stacks = ref<StackDto[]>([])
const buckets = ref<BucketDto[]>([])
const loading = ref(true)

// A bucket's identity is (source, name); join with a separator that can't appear in either for select values.
const SEP = '\u001f'
const bucketKey = (b: { sourceName: string; bucketName: string }) =>
  `${b.sourceName}${SEP}${b.bucketName}`
const parseBucketKey = (key: string): BucketRefDto => {
  const [sourceName = '', bucketName = ''] = key.split(SEP)
  return { sourceName, bucketName }
}

const editDialog = ref(false)
const editing = ref<CustomerDto | null>(null)
const form = ref({ name: '', notes: '' })

const SHARED = 'shared'
type AssignTarget = CustomerDto | typeof SHARED

const stacksDialog = ref(false)
const stacksTarget = ref<AssignTarget | null>(null)
const selectedStacks = ref<string[]>([])

const sharedStackNames = computed(() =>
  stacks.value.filter((s) => s.isShared).map((s) => s.stackName),
)

const bucketsDialog = ref(false)
const bucketsTarget = ref<AssignTarget | null>(null)
const selectedBuckets = ref<string[]>([])

const sharedBuckets = computed(() => buckets.value.filter((b) => b.isShared))

async function refresh() {
  loading.value = true
  try {
    ;[customers.value, stacks.value, buckets.value] = await Promise.all([
      getCustomers(),
      getStacks(),
      getBuckets(),
    ])
  } finally {
    loading.value = false
  }
}

onMounted(refresh)

function targetName(target: AssignTarget | null): string | undefined {
  return target === SHARED ? 'shared infrastructure' : target?.name
}

// Items owned elsewhere are shown but flagged, since assigning moves them here.
function otherOwner(
  item: { customerId: number | null; customerName: string | null; isShared: boolean },
  target: AssignTarget | null,
): string | null {
  if (item.isShared) return target === SHARED ? null : 'shared infrastructure'
  if (item.customerName === null) return null
  return target !== SHARED && item.customerId === target?.id ? null : item.customerName
}

function optionTitle(label: string, owner: string | null): string {
  return owner ? `${label} (currently ${owner})` : label
}

const stackOptions = computed(() =>
  stacks.value.map((s) => ({
    title: optionTitle(s.stackName, otherOwner(s, stacksTarget.value)),
    value: s.stackName,
  })),
)

const bucketOptions = computed(() =>
  buckets.value.map((b) => ({
    title: optionTitle(`${b.sourceName}/${b.bucketName}`, otherOwner(b, bucketsTarget.value)),
    value: bucketKey(b),
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

const deleteOpen = ref(false)
const deleteTarget = ref<CustomerDto | null>(null)
const deleting = ref(false)
const deleteError = ref('')

function removeCustomer(customer: CustomerDto) {
  deleteTarget.value = customer
  deleteError.value = ''
  deleteOpen.value = true
}

async function confirmDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  deleteError.value = ''
  try {
    await deleteCustomer(deleteTarget.value.id)
    deleteOpen.value = false
    await refresh()
  } catch (e) {
    deleteError.value = apiErrorMessage(e, 'Deleting the customer failed.')
  } finally {
    deleting.value = false
  }
}

function openStacks(customer: CustomerDto) {
  stacksTarget.value = customer
  selectedStacks.value = [...customer.stackNames]
  stacksDialog.value = true
}

function openSharedStacks() {
  stacksTarget.value = SHARED
  selectedStacks.value = [...sharedStackNames.value]
  stacksDialog.value = true
}

async function saveStacks() {
  const target = stacksTarget.value
  if (!target) return
  if (target === SHARED) {
    await setSharedStacks(selectedStacks.value)
  } else {
    await setCustomerStacks(target.id, selectedStacks.value)
  }
  stacksDialog.value = false
  await refresh()
}

function openBuckets(customer: CustomerDto) {
  bucketsTarget.value = customer
  selectedBuckets.value = customer.buckets.map(bucketKey)
  bucketsDialog.value = true
}

function openSharedBuckets() {
  bucketsTarget.value = SHARED
  selectedBuckets.value = sharedBuckets.value.map(bucketKey)
  bucketsDialog.value = true
}

async function saveBuckets() {
  const target = bucketsTarget.value
  if (!target) return
  const refs = selectedBuckets.value.map(parseBucketKey)
  if (target === SHARED) {
    await setSharedBuckets(refs)
  } else {
    await setCustomerBuckets(target.id, refs)
  }
  bucketsDialog.value = false
  await refresh()
}
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4">
      <h1 class="text-h5">Customers</h1>
      <v-spacer />
      <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New Customer</v-btn>
    </div>

    <v-progress-linear v-if="loading" indeterminate />

    <v-card variant="outlined" class="mb-4">
      <div class="d-flex align-start ga-4 pa-4">
        <div class="flex-grow-1">
          <div class="d-flex align-center ga-2 mb-2">
            <span class="text-subtitle-1 font-weight-medium">Shared infrastructure</span>
            <v-chip size="x-small" variant="tonal">Not billed</v-chip>
          </div>
          <v-chip
            v-for="name in sharedStackNames"
            :key="name"
            size="x-small"
            class="ma-1"
            variant="tonal"
          >
            {{ name }}
          </v-chip>
          <v-chip
            v-for="b in sharedBuckets"
            :key="bucketKey(b)"
            size="x-small"
            class="ma-1"
            variant="tonal"
            prepend-icon="mdi-database"
          >
            {{ b.bucketName }}
          </v-chip>
          <span
            v-if="sharedStackNames.length === 0 && sharedBuckets.length === 0"
            class="text-medium-emphasis"
            >None</span
          >
        </div>
        <div class="text-no-wrap">
          <v-btn
            size="small"
            variant="text"
            icon="mdi-layers-edit"
            aria-label="Edit shared infrastructure stacks"
            @click="openSharedStacks"
          />
          <v-btn
            size="small"
            variant="text"
            icon="mdi-database-edit"
            aria-label="Edit shared infrastructure buckets"
            @click="openSharedBuckets"
          />
        </div>
      </div>
    </v-card>

    <v-card variant="outlined">
      <v-table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Stacks</th>
            <th>Buckets</th>
            <th>Notes</th>
            <th class="text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="customer in customers" :key="customer.id">
            <td class="font-weight-medium">{{ customer.name }}</td>
            <td>
              <v-chip
                v-for="name in customer.stackNames"
                :key="name"
                size="x-small"
                class="ma-1"
                variant="tonal"
              >
                {{ name }}
              </v-chip>
              <span v-if="customer.stackNames.length === 0" class="text-medium-emphasis">None</span>
            </td>
            <td>
              <v-chip
                v-for="b in customer.buckets"
                :key="`${b.sourceName}/${b.bucketName}`"
                size="x-small"
                class="ma-1"
                variant="tonal"
                prepend-icon="mdi-database"
              >
                {{ b.bucketName }}
              </v-chip>
              <span v-if="customer.buckets.length === 0" class="text-medium-emphasis">None</span>
            </td>
            <td class="text-medium-emphasis">{{ customer.notes }}</td>
            <td class="text-right text-no-wrap">
              <v-btn
                size="small"
                variant="text"
                icon="mdi-layers-edit"
                @click="openStacks(customer)"
              />
              <v-btn
                size="small"
                variant="text"
                icon="mdi-database-edit"
                @click="openBuckets(customer)"
              />
              <v-btn size="small" variant="text" icon="mdi-pencil" @click="openEdit(customer)" />
              <v-btn
                size="small"
                variant="text"
                icon="mdi-delete"
                color="error"
                @click="removeCustomer(customer)"
              />
            </td>
          </tr>
          <tr v-if="!loading && customers.length === 0">
            <td colspan="5" class="text-center text-medium-emphasis py-6">No customers yet.</td>
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
        <v-card-title>Assign stacks to {{ targetName(stacksTarget) }}</v-card-title>
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

    <v-dialog v-model="bucketsDialog" max-width="520">
      <v-card>
        <v-card-title>Assign buckets to {{ targetName(bucketsTarget) }}</v-card-title>
        <v-card-text>
          <v-select
            v-model="selectedBuckets"
            :items="bucketOptions"
            label="Buckets"
            multiple
            chips
            closable-chips
            :hint="buckets.length === 0 ? 'No buckets found.' : undefined"
            persistent-hint
          />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="bucketsDialog = false">Cancel</v-btn>
          <v-btn color="primary" @click="saveBuckets">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <ConfirmDialog
      v-model="deleteOpen"
      title="Delete customer"
      :busy="deleting"
      :error="deleteError"
      @confirm="confirmDelete"
    >
      Delete <strong>{{ deleteTarget?.name }}</strong
      >? Its stacks and buckets become unassigned.
    </ConfirmDialog>
  </div>
</template>
