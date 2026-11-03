<script setup lang="ts">
import { ref, onMounted } from 'vue'
import type { S3SourceDto, S3SourceEditDto, S3SourceKind } from '@/types/s3'
import type { AssignmentDto, BucketDto, CustomerDto } from '@/types/customer'
import { getBuckets, setBucketAssignment } from '@/api/bucketsApi'
import { getCustomers } from '@/api/customersApi'
import { formatBytes } from '@/utils/format'
import AssignmentSelect from '@/components/AssignmentSelect.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { apiErrorMessage } from '@/api/http'
import {
  getS3Sources,
  createS3Source,
  updateS3Source,
  deleteS3Source,
  testS3Source,
  refreshS3Usage,
} from '@/api/s3SourcesApi'

const sources = ref<S3SourceDto[]>([])
const loading = ref(true)
const polling = ref(false)
const pollResult = ref<string | null>(null)

const dialog = ref(false)
const editing = ref<S3SourceDto | null>(null)
const form = ref<S3SourceEditDto>(blankForm())
const testResult = ref<{ ok: boolean; message: string } | null>(null)
const testing = ref(false)
const saving = ref(false)

const kindOptions: { title: string; value: S3SourceKind }[] = [
  { title: 'MinIO / RustFS (admin data-usage)', value: 'Minio' },
  { title: 'Generic S3 (object listing)', value: 'GenericS3' },
]

function blankForm(): S3SourceEditDto {
  return {
    name: '',
    endpoint: '',
    kind: 'Minio',
    accessKey: '',
    secretKey: '',
    region: 'us-east-1',
    enabled: true,
  }
}

const buckets = ref<BucketDto[]>([])
const customers = ref<CustomerDto[]>([])
const assigningBucket = ref<string | null>(null)

const bucketKey = (b: BucketDto) => `${b.sourceName}/${b.bucketName}`

async function refresh() {
  loading.value = true
  try {
    ;[sources.value, buckets.value, customers.value] = await Promise.all([
      getS3Sources(),
      getBuckets(),
      getCustomers(),
    ])
  } finally {
    loading.value = false
  }
}

async function assignBucket(bucket: BucketDto, assignment: AssignmentDto) {
  assigningBucket.value = bucketKey(bucket)
  try {
    await setBucketAssignment(bucket, assignment)
    buckets.value = await getBuckets()
  } finally {
    assigningBucket.value = null
  }
}

onMounted(refresh)

// Sources are otherwise only polled on the central's slow S3 interval, so buckets added since the last
// cycle would not be assignable until it comes around again.
async function pollNow() {
  polling.value = true
  pollResult.value = null
  try {
    const result = await refreshS3Usage()
    pollResult.value = `Found ${result.bucketCount} buckets.`
    buckets.value = await getBuckets()
  } finally {
    polling.value = false
  }
}

function openCreate() {
  editing.value = null
  form.value = blankForm()
  testResult.value = null
  dialog.value = true
}

function openEdit(source: S3SourceDto) {
  editing.value = source
  form.value = {
    name: source.name,
    endpoint: source.endpoint,
    kind: source.kind,
    accessKey: source.accessKey,
    secretKey: '',
    region: source.region,
    enabled: source.enabled,
  }
  testResult.value = null
  dialog.value = true
}

// On edit, an empty secret means "keep the stored one"; on create it is required.
function payload(): S3SourceEditDto {
  return { ...form.value, secretKey: form.value.secretKey || null }
}

async function runTest() {
  testing.value = true
  testResult.value = null
  try {
    const result = await testS3Source(payload(), editing.value?.id)
    testResult.value = result.ok
      ? { ok: true, message: `Connected. ${result.bucketCount} buckets.` }
      : { ok: false, message: result.error ?? 'Connection failed.' }
  } finally {
    testing.value = false
  }
}

async function save() {
  saving.value = true
  try {
    if (editing.value) {
      await updateS3Source(editing.value.id, payload())
    } else {
      await createS3Source(payload())
    }
    dialog.value = false
    await refresh()
  } finally {
    saving.value = false
  }
}

const deleteOpen = ref(false)
const deleteTarget = ref<S3SourceDto | null>(null)
const deleting = ref(false)
const deleteError = ref('')

function remove(source: S3SourceDto) {
  deleteTarget.value = source
  deleteError.value = ''
  deleteOpen.value = true
}

async function confirmDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  deleteError.value = ''
  try {
    await deleteS3Source(deleteTarget.value.id)
    deleteOpen.value = false
    await refresh()
  } catch (e) {
    deleteError.value = apiErrorMessage(e, 'Deleting the source failed.')
  } finally {
    deleting.value = false
  }
}

const canSave = () =>
  form.value.name.trim().length > 0 &&
  form.value.endpoint.trim().length > 0 &&
  form.value.accessKey.trim().length > 0 &&
  (editing.value !== null || (form.value.secretKey ?? '').length > 0)
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4">
      <h1 class="text-h5">Storage</h1>
      <v-spacer />
      <v-btn
        variant="text"
        prepend-icon="mdi-refresh"
        :loading="polling"
        :disabled="sources.length === 0"
        class="mr-2"
        @click="pollNow"
      >
        Refresh usage
      </v-btn>
      <v-btn color="primary" prepend-icon="mdi-plus" @click="openCreate">New Source</v-btn>
    </div>

    <v-alert
      v-if="pollResult"
      type="success"
      variant="tonal"
      density="compact"
      closable
      class="mb-4"
      @click:close="pollResult = null"
    >
      {{ pollResult }}
    </v-alert>

    <v-progress-linear v-if="loading" indeterminate />

    <v-card variant="outlined">
      <v-table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Endpoint</th>
            <th>Type</th>
            <th>Access key</th>
            <th>Status</th>
            <th class="text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="source in sources" :key="source.id">
            <td class="font-weight-medium">{{ source.name }}</td>
            <td class="text-medium-emphasis">{{ source.endpoint }}</td>
            <td>{{ source.kind === 'Minio' ? 'MinIO / RustFS' : 'Generic S3' }}</td>
            <td class="text-medium-emphasis">{{ source.accessKey }}</td>
            <td>
              <v-chip
                size="x-small"
                :color="source.enabled ? 'success' : undefined"
                variant="tonal"
              >
                {{ source.enabled ? 'Enabled' : 'Disabled' }}
              </v-chip>
            </td>
            <td class="text-right text-no-wrap">
              <v-btn size="small" variant="text" icon="mdi-pencil" @click="openEdit(source)" />
              <v-btn
                size="small"
                variant="text"
                icon="mdi-delete"
                color="error"
                @click="remove(source)"
              />
            </td>
          </tr>
          <tr v-if="!loading && sources.length === 0">
            <td colspan="6" class="text-center text-medium-emphasis py-6">No sources yet.</td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <h2 class="text-h6 mt-8 mb-3">Buckets</h2>
    <v-card variant="outlined">
      <v-table>
        <thead>
          <tr>
            <th>Bucket</th>
            <th>Source</th>
            <th class="text-right">Size</th>
            <th class="text-right">Objects</th>
            <th>Assigned to</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="bucket in buckets" :key="bucketKey(bucket)">
            <td class="font-weight-medium">
              {{ bucket.bucketName }}
              <v-chip v-if="!bucket.isActive" size="x-small" variant="tonal" class="ml-2"
                >Not seen</v-chip
              >
            </td>
            <td class="text-medium-emphasis">{{ bucket.sourceName }}</td>
            <td class="text-right">{{ formatBytes(bucket.sizeBytes) }}</td>
            <td class="text-right">{{ bucket.objectCount.toLocaleString() }}</td>
            <td class="bucket-assignment py-2">
              <AssignmentSelect
                :assignment="bucket"
                :customers="customers"
                :loading="assigningBucket === bucketKey(bucket)"
                @change="(assignment) => assignBucket(bucket, assignment)"
              />
            </td>
          </tr>
          <tr v-if="!loading && buckets.length === 0">
            <td colspan="5" class="text-center text-medium-emphasis py-6">No buckets yet.</td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <v-dialog v-model="dialog" max-width="560">
      <v-card>
        <v-card-title>{{ editing ? 'Edit S3 Source' : 'New S3 Source' }}</v-card-title>
        <v-card-text>
          <v-text-field
            v-model="form.name"
            label="Name"
            :disabled="editing !== null"
            hint="Can't be changed later"
            persistent-hint
            class="mb-2"
          />
          <v-text-field
            v-model="form.endpoint"
            label="Endpoint"
            placeholder="http://minio:9000"
            class="mb-2"
          />
          <v-select v-model="form.kind" :items="kindOptions" label="Type" class="mb-2" />
          <v-text-field v-model="form.accessKey" label="Access key" class="mb-2" />
          <v-text-field
            v-model="form.secretKey"
            label="Secret key"
            type="password"
            :placeholder="editing ? 'Unchanged' : undefined"
            autocomplete="new-password"
            class="mb-2"
          />
          <v-text-field v-model="form.region" label="Region" class="mb-2" />
          <v-switch v-model="form.enabled" label="Enabled" color="primary" hide-details />

          <v-alert
            v-if="testResult"
            :type="testResult.ok ? 'success' : 'error'"
            variant="tonal"
            density="compact"
            class="mt-3"
          >
            {{ testResult.message }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-btn variant="text" :loading="testing" :disabled="!canSave()" @click="runTest">
            Test connection
          </v-btn>
          <v-spacer />
          <v-btn variant="text" @click="dialog = false">Cancel</v-btn>
          <v-btn color="primary" :loading="saving" :disabled="!canSave()" @click="save">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <ConfirmDialog
      v-model="deleteOpen"
      title="Delete S3 source"
      :busy="deleting"
      :error="deleteError"
      @confirm="confirmDelete"
    >
      Delete <strong>{{ deleteTarget?.name }}</strong
      >? Its buckets lose their assignments.
    </ConfirmDialog>
  </div>
</template>

<style scoped>
.bucket-assignment {
  min-width: 220px;
}
</style>
