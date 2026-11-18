<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import type {
  BackupSettingsDto,
  BackupTargetCheckDto,
  BackupTargetDto,
  BackupTargetEditDto,
  DayOfWeek,
  S3Addressing,
} from '@/types/backups'
import {
  getBackupSettings,
  getBackupTarget,
  runBackupMaintenance,
  testBackupTarget,
  updateBackupSettings,
  updateBackupTarget,
} from '@/api/backupsApi'
import { apiErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/authStore'
import { absoluteTime, formatBytes, relativeTime } from '@/utils/format'

const authStore = useAuthStore()
const isAdmin = computed(() => !!authStore.user?.isAdministrator)

const target = ref<BackupTargetDto | null>(null)
const targetForm = reactive<BackupTargetEditDto>({
  endpoint: '',
  region: 'us-east-1',
  bucket: '',
  prefix: 'track/',
  addressing: 'Auto',
  agentAccessKey: '',
  agentSecretKey: '',
  maintenanceAccessKey: '',
  maintenanceSecretKey: '',
})
const checks = ref<BackupTargetCheckDto[] | null>(null)
const testing = ref(false)
const savingTarget = ref(false)
const targetMessage = ref<{ text: string; error: boolean } | null>(null)

const settings = ref<BackupSettingsDto | null>(null)
const backupTime = ref('02:00')
const pruneTime = ref('06:00')
const savingSettings = ref(false)
const settingsMessage = ref<{ text: string; error: boolean } | null>(null)
const maintenanceQueued = ref(false)

const addressingOptions: { title: string; value: S3Addressing }[] = [
  { title: 'Auto', value: 'Auto' },
  { title: 'Virtual-host style', value: 'VirtualHost' },
  { title: 'Path style', value: 'Path' },
]
const days: DayOfWeek[] = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
]

const toTime = (minutes: number) =>
  `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`
const toMinutes = (time: string) => {
  const [hours = 0, minutes = 0] = time.split(':').map(Number)
  return hours * 60 + minutes
}

async function load() {
  const [t, s] = await Promise.all([getBackupTarget(), getBackupSettings()])
  target.value = t
  Object.assign(targetForm, {
    endpoint: t.endpoint,
    region: t.region,
    bucket: t.bucket,
    prefix: t.prefix,
    addressing: t.addressing,
    agentAccessKey: t.agentAccessKey,
    agentSecretKey: '',
    maintenanceAccessKey: t.maintenanceAccessKey ?? '',
    maintenanceSecretKey: '',
  })
  settings.value = s
  backupTime.value = toTime(s.backupMinuteUtc)
  pruneTime.value = toTime(s.pruneMinuteUtc)
}

onMounted(load)

function targetPayload(): BackupTargetEditDto {
  return {
    ...targetForm,
    agentSecretKey: targetForm.agentSecretKey || null,
    maintenanceAccessKey: targetForm.maintenanceAccessKey?.trim() || null,
    maintenanceSecretKey: targetForm.maintenanceSecretKey || null,
  }
}

async function test() {
  testing.value = true
  checks.value = null
  targetMessage.value = null
  try {
    checks.value = await testBackupTarget(targetPayload())
  } catch (e) {
    targetMessage.value = { text: apiErrorMessage(e, 'The test couldn’t run.'), error: true }
  } finally {
    testing.value = false
  }
}

async function saveTarget() {
  savingTarget.value = true
  targetMessage.value = null
  try {
    await updateBackupTarget(targetPayload())
    targetMessage.value = { text: 'Saved.', error: false }
    await load()
  } catch (e) {
    targetMessage.value = { text: apiErrorMessage(e, 'Saving failed.'), error: true }
  } finally {
    savingTarget.value = false
  }
}

async function saveSettings() {
  if (!settings.value) return
  savingSettings.value = true
  settingsMessage.value = null
  try {
    await updateBackupSettings({
      ...settings.value,
      backupMinuteUtc: toMinutes(backupTime.value),
      pruneMinuteUtc: toMinutes(pruneTime.value),
      heartbeatUrl: settings.value.heartbeatUrl?.trim() || null,
    })
    settingsMessage.value = { text: 'Saved.', error: false }
  } catch (e) {
    settingsMessage.value = { text: apiErrorMessage(e, 'Saving failed.'), error: true }
  } finally {
    savingSettings.value = false
  }
}

async function maintainNow() {
  await runBackupMaintenance()
  maintenanceQueued.value = true
}

function repositoryName(key: string, customerName: string | null) {
  if (customerName) return customerName
  if (key === 'shared') return 'Shared infrastructure'
  if (key === 'unassigned') return 'Unassigned stacks'
  return key
}
</script>

<template>
  <div class="settings-grid">
    <v-card variant="outlined" class="pa-4">
      <div class="d-flex align-center mb-4">
        <h2 class="text-subtitle-1 font-weight-medium">Off-site target</h2>
        <v-spacer />
        <v-btn variant="tonal" size="small" :loading="testing" @click="test">Test</v-btn>
      </div>
      <div class="form-grid">
        <v-text-field
          v-model="targetForm.endpoint"
          label="Endpoint"
          placeholder="https://s3.example.com"
          class="wide"
          :disabled="!isAdmin"
        />
        <v-text-field v-model="targetForm.bucket" label="Bucket" :disabled="!isAdmin" />
        <v-text-field v-model="targetForm.prefix" label="Prefix" :disabled="!isAdmin" />
        <v-text-field v-model="targetForm.region" label="Region" :disabled="!isAdmin" />
        <v-select
          v-model="targetForm.addressing"
          :items="addressingOptions"
          label="Addressing"
          :disabled="!isAdmin"
        />
        <v-text-field
          v-model="targetForm.agentAccessKey"
          label="Agent access key"
          :disabled="!isAdmin"
        />
        <v-text-field
          v-model="targetForm.agentSecretKey"
          label="Agent secret key"
          type="password"
          autocomplete="new-password"
          :placeholder="target?.hasAgentSecret ? 'Unchanged' : undefined"
          :disabled="!isAdmin"
        />
        <v-text-field
          v-model="targetForm.maintenanceAccessKey"
          label="Maintenance access key"
          hint="Used for prune"
          persistent-hint
          :disabled="!isAdmin"
        />
        <v-text-field
          v-model="targetForm.maintenanceSecretKey"
          label="Maintenance secret key"
          type="password"
          autocomplete="new-password"
          :placeholder="target?.hasMaintenanceSecret ? 'Unchanged' : undefined"
          :disabled="!isAdmin"
        />
      </div>

      <div v-if="checks" class="checks mt-2">
        <div v-for="check in checks" :key="check.label" class="d-flex align-start ga-2 text-body-2">
          <v-icon
            size="small"
            :color="check.ok ? 'success' : 'warning'"
            :icon="check.ok ? 'mdi-check-circle' : 'mdi-alert-circle'"
          />
          <div>
            {{ check.label }}
            <div v-if="check.detail" class="text-caption text-medium-emphasis">
              {{ check.detail }}
            </div>
          </div>
        </div>
      </div>

      <v-alert
        v-if="targetMessage"
        :type="targetMessage.error ? 'error' : 'success'"
        variant="tonal"
        density="compact"
        class="mt-3"
      >
        {{ targetMessage.text }}
      </v-alert>

      <div class="d-flex mt-4">
        <v-spacer />
        <v-btn
          color="primary"
          variant="flat"
          :disabled="!isAdmin"
          :loading="savingTarget"
          @click="saveTarget"
          >Save</v-btn
        >
      </div>
    </v-card>

    <div class="d-flex flex-column ga-4">
      <v-card v-if="settings" variant="outlined" class="pa-4">
        <div class="d-flex align-center mb-4">
          <h2 class="text-subtitle-1 font-weight-medium">Schedule</h2>
          <v-spacer />
          <v-switch
            v-model="settings.paused"
            label="Paused"
            color="warning"
            density="compact"
            hide-details
          />
        </div>
        <div class="form-grid">
          <v-text-field v-model="backupTime" type="time" label="Backups at (UTC)" />
          <v-text-field
            v-model.number="settings.spreadMinutes"
            type="number"
            min="0"
            label="Spread over (minutes)"
          />
          <v-text-field
            v-model.number="settings.maxJobsPerNode"
            type="number"
            min="1"
            label="Jobs per node"
          />
          <v-text-field
            v-model.number="settings.maxJobsTotal"
            type="number"
            min="1"
            label="Jobs in total"
          />
          <v-text-field
            v-model.number="settings.keepDaily"
            type="number"
            min="1"
            label="Keep daily"
          />
          <v-text-field
            v-model.number="settings.keepWeekly"
            type="number"
            min="0"
            label="Keep weekly"
          />
          <v-text-field
            v-model.number="settings.keepMonthly"
            type="number"
            min="0"
            label="Keep monthly"
          />
          <v-text-field
            v-model.number="settings.staleAfterHours"
            type="number"
            min="1"
            label="Stale after (hours)"
          />
          <v-select v-model="settings.pruneDay" :items="days" label="Prune on" />
          <v-text-field v-model="pruneTime" type="time" label="Prune at (UTC)" />
          <v-text-field
            v-model.number="settings.checkPercent"
            type="number"
            min="1"
            max="100"
            label="Verify monthly (% of data)"
          />
          <v-text-field
            v-model.number="settings.hookTimeoutMinutes"
            type="number"
            min="1"
            label="Hook timeout (minutes)"
          />
          <v-text-field
            v-model="settings.heartbeatUrl"
            label="Heartbeat URL"
            placeholder="https://hc-ping.com/…"
            class="wide"
          />
        </div>
        <v-alert
          v-if="settingsMessage"
          :type="settingsMessage.error ? 'error' : 'success'"
          variant="tonal"
          density="compact"
          class="mt-3"
        >
          {{ settingsMessage.text }}
        </v-alert>
        <div class="d-flex mt-4">
          <v-spacer />
          <v-btn color="primary" variant="flat" :loading="savingSettings" @click="saveSettings"
            >Save</v-btn
          >
        </div>
      </v-card>

      <v-card v-if="target" variant="outlined" class="pa-4">
        <div class="d-flex align-center">
          <h2 class="text-subtitle-1 font-weight-medium">Master key</h2>
          <v-spacer />
          <v-chip
            size="small"
            :color="target.masterKeySet ? 'success' : 'warning'"
            variant="tonal"
            >{{ target.masterKeySet ? 'Set' : 'Not set' }}</v-chip
          >
        </div>
        <div class="text-body-2 text-medium-emphasis mt-2">
          <code v-if="target.masterKeyFromEnvironment">Track__Backups__MasterKey</code>
          <template v-else-if="target.masterKeySet"
            >Swarm secret <code>{{ target.masterKeySecretName }}</code></template
          >
          <template v-else
            ><code>Track__Backups__MasterKey</code> or Swarm secret
            <code>{{ target.masterKeySecretName }}</code></template
          >
        </div>
      </v-card>
    </div>
  </div>

  <v-card v-if="target" variant="outlined" class="mt-4">
    <div class="d-flex align-center pa-4 pb-2">
      <h2 class="text-subtitle-1 font-weight-medium">Repositories</h2>
      <v-spacer />
      <v-btn
        variant="text"
        size="small"
        :disabled="maintenanceQueued || target.repositories.length === 0"
        @click="maintainNow"
        >{{ maintenanceQueued ? 'Maintenance queued' : 'Prune and verify now' }}</v-btn
      >
    </div>
    <v-table density="comfortable">
      <thead>
        <tr>
          <th>Repository</th>
          <th class="text-right">Size</th>
          <th>Pruned</th>
          <th>Verified</th>
          <th></th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in target.repositories" :key="r.key">
          <td>
            <div class="font-weight-medium">{{ repositoryName(r.key, r.customerName) }}</div>
            <div class="text-caption text-medium-emphasis">{{ target.prefix }}{{ r.key }}</div>
          </td>
          <td class="text-right">{{ formatBytes(r.sizeBytes) }}</td>
          <td :title="absoluteTime(r.lastPrunedAt)">{{ relativeTime(r.lastPrunedAt) }}</td>
          <td :title="absoluteTime(r.lastCheckedAt)">{{ relativeTime(r.lastCheckedAt) }}</td>
          <td class="text-caption text-error">{{ r.lastMaintenanceError }}</td>
        </tr>
        <tr v-if="target.repositories.length === 0">
          <td colspan="5" class="text-center text-medium-emphasis py-6">No repositories yet.</td>
        </tr>
      </tbody>
    </v-table>
  </v-card>
</template>

<style scoped>
.settings-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(340px, 1fr));
  gap: 16px;
  align-items: start;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 12px;
}

.form-grid .wide {
  grid-column: 1 / -1;
}

.checks {
  display: grid;
  gap: 6px;
}
</style>
