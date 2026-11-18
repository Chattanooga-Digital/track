<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { BackupSnapshotDto, BackupVolumeDto } from '@/types/backups'
import { getSnapshots, restoreSnapshot, snapshotDownloadUrl } from '@/api/backupsApi'
import { apiErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/authStore'
import { formatBytes } from '@/utils/format'

const props = defineProps<{
  volume: BackupVolumeDto | null
  readyNodes: string[]
}>()

const emit = defineEmits<{
  'update:volume': [value: BackupVolumeDto | null]
  started: [message: string]
}>()

type Mode = 'NewVolume' | 'Replace' | 'Download'

const authStore = useAuthStore()
const snapshots = ref<BackupSnapshotDto[]>([])
const loading = ref(false)
const selectedId = ref<number | null>(null)
const mode = ref<Mode>('NewVolume')
const node = ref<string | null>(null)
const confirmName = ref('')
const showAll = ref(false)
const submitting = ref(false)
const error = ref('')

const open = computed({
  get: () => props.volume !== null,
  set: (value) => {
    if (!value) emit('update:volume', null)
  },
})

watch(
  () => props.volume,
  async (volume) => {
    if (!volume) return
    snapshots.value = []
    selectedId.value = null
    mode.value = 'NewVolume'
    confirmName.value = ''
    showAll.value = false
    error.value = ''
    node.value =
      volume.nodeName && props.readyNodes.includes(volume.nodeName)
        ? volume.nodeName
        : (props.readyNodes[0] ?? null)
    loading.value = true
    try {
      snapshots.value = await getSnapshots(volume.volumeName)
      selectedId.value = snapshots.value[0]?.id ?? null
    } finally {
      loading.value = false
    }
  },
)

const shown = computed(() => (showAll.value ? snapshots.value : snapshots.value.slice(0, 5)))
const selected = computed(() => snapshots.value.find((s) => s.id === selectedId.value) ?? null)
const isAdmin = computed(() => !!authStore.user?.isAdministrator)

const plan = computed(() => {
  const v = props.volume
  if (!v) return []
  const services = v.services.join(', ') || 'the services using it'
  const steps = ['Snapshot current data', `Stop ${services}`, `Restore on ${v.nodeName}`]
  steps.push(`Start ${services}`)
  if (v.hasRestoreHooks) steps.push('Run the restore hook')
  return steps
})

const canSubmit = computed(() => {
  if (!selected.value || submitting.value) return false
  if (mode.value === 'NewVolume') return node.value !== null
  if (mode.value === 'Replace')
    return (
      isAdmin.value &&
      !props.volume?.replaceBlockedReason &&
      confirmName.value === props.volume?.volumeName
    )
  return false
})

async function submit() {
  if (!selected.value || !props.volume || mode.value === 'Download') return
  submitting.value = true
  error.value = ''
  try {
    await restoreSnapshot({
      snapshotId: selected.value.id,
      mode: mode.value,
      nodeName: mode.value === 'NewVolume' ? node.value : null,
      confirmVolumeName: mode.value === 'Replace' ? confirmName.value : null,
    })
    emit(
      'started',
      mode.value === 'Replace'
        ? `Replacing ${props.volume.volumeName}.`
        : `Restoring ${props.volume.volumeName} to a new volume on ${node.value}.`,
    )
    open.value = false
  } catch (e) {
    error.value = apiErrorMessage(e, 'The restore couldn’t start.')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <v-dialog v-model="open" max-width="680" :persistent="submitting">
    <v-card v-if="volume">
      <v-card-title class="d-flex align-center">
        <span>Restore {{ volume.volumeName }}</span>
        <v-spacer />
        <v-btn icon="mdi-close" variant="text" size="small" @click="open = false" />
      </v-card-title>
      <v-card-subtitle class="mt-n2">
        {{
          [volume.stackName, volume.services.join(', '), volume.nodeName]
            .filter(Boolean)
            .join(' · ')
        }}
      </v-card-subtitle>

      <v-card-text>
        <div class="text-caption text-medium-emphasis mb-1">Snapshot</div>
        <v-progress-linear v-if="loading" indeterminate class="mb-2" />
        <div v-else-if="snapshots.length === 0" class="text-medium-emphasis py-3">
          No snapshots yet.
        </div>
        <v-radio-group v-else v-model="selectedId" hide-details density="compact">
          <v-radio v-for="s in shown" :key="s.id" :value="s.id">
            <template #label>
              <div class="d-flex align-center flex-wrap ga-2 w-100">
                <span class="snapshot-time">{{ new Date(s.time).toLocaleString() }}</span>
                <span class="text-caption text-medium-emphasis">
                  {{ s.images.join(', ') }}
                  <template v-if="s.bytesAdded !== null">
                    · +{{ formatBytes(s.bytesAdded) }}</template
                  >
                </span>
                <v-chip v-if="s.preRestore" size="x-small" variant="tonal">Before restore</v-chip>
                <v-chip v-if="s.differentImage" size="x-small" color="warning" variant="tonal"
                  >Different image</v-chip
                >
                <v-chip v-if="s.paths.length" size="x-small" variant="tonal"
                  >Only {{ s.paths.join(', ') }}</v-chip
                >
              </div>
            </template>
          </v-radio>
        </v-radio-group>
        <v-btn
          v-if="snapshots.length > 5 && !showAll"
          variant="text"
          size="small"
          class="mt-1"
          @click="showAll = true"
          >Show all {{ snapshots.length }}</v-btn
        >

        <div class="text-caption text-medium-emphasis mt-4 mb-1">Restore as</div>
        <v-btn-toggle v-model="mode" mandatory density="comfortable" variant="outlined" divided>
          <v-btn value="NewVolume">New volume</v-btn>
          <v-btn value="Replace" :color="mode === 'Replace' ? 'error' : undefined"
            >Replace live data</v-btn
          >
          <v-btn value="Download">Download</v-btn>
        </v-btn-toggle>

        <div v-if="mode === 'NewVolume'" class="mt-4">
          <v-select
            v-model="node"
            :items="readyNodes"
            label="Node"
            density="comfortable"
            variant="outlined"
            hide-details
          />
        </div>

        <div v-else-if="mode === 'Replace'" class="mt-4">
          <v-alert
            v-if="volume.replaceBlockedReason"
            type="warning"
            variant="tonal"
            density="compact"
          >
            {{ volume.replaceBlockedReason }}
          </v-alert>
          <v-alert v-else-if="!isAdmin" type="info" variant="tonal" density="compact">
            Only administrators can replace live data.
          </v-alert>
          <template v-else>
            <ol class="plan text-body-2 mb-4">
              <li v-for="step in plan" :key="step">{{ step }}</li>
            </ol>
            <v-text-field
              v-model="confirmName"
              :label="`Type ${volume.volumeName} to confirm`"
              density="comfortable"
              variant="outlined"
              autocomplete="off"
              hide-details
            />
          </template>
        </div>

        <div v-else class="mt-4">
          <v-btn
            :href="selected ? snapshotDownloadUrl(selected.id) : undefined"
            :disabled="!selected"
            prepend-icon="mdi-download"
            variant="tonal"
            >Download .tar</v-btn
          >
        </div>

        <v-alert v-if="error" type="error" variant="tonal" density="compact" class="mt-4">
          {{ error }}
        </v-alert>
      </v-card-text>

      <v-card-actions v-if="mode !== 'Download'">
        <v-spacer />
        <v-btn variant="text" :disabled="submitting" @click="open = false">Cancel</v-btn>
        <v-btn
          :color="mode === 'Replace' ? 'error' : 'primary'"
          variant="flat"
          :disabled="!canSubmit"
          :loading="submitting"
          @click="submit"
          >{{ mode === 'Replace' ? 'Replace live data' : 'Restore' }}</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.snapshot-time {
  min-width: 170px;
  font-variant-numeric: tabular-nums;
}

.plan {
  padding-left: 20px;
}
</style>
