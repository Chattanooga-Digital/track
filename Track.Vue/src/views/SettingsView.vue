<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import type { EmailSettingsDto, EmailSettingsEditDto, MailSecurity } from '@/types/settings'
import { getEmailSettings, testEmailSettings, updateEmailSettings } from '@/api/settingsApi'
import { apiErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/authStore'

const authStore = useAuthStore()

const email = ref<EmailSettingsDto | null>(null)
const form = reactive<EmailSettingsEditDto>({
  enabled: true,
  host: '',
  port: 587,
  security: 'Auto',
  userName: '',
  password: '',
  fromName: 'Track',
  fromAddress: '',
  siteUrl: '',
})
const testTo = ref(authStore.user?.email ?? '')
const saving = ref(false)
const testing = ref(false)
const message = ref<{ text: string; error: boolean } | null>(null)

const securityOptions: { title: string; value: MailSecurity }[] = [
  { title: 'Auto', value: 'Auto' },
  { title: 'STARTTLS', value: 'StartTls' },
  { title: 'TLS', value: 'SslOnConnect' },
  { title: 'None', value: 'None' },
]

async function load() {
  const settings = await getEmailSettings()
  email.value = settings
  Object.assign(form, {
    enabled: settings.enabled,
    host: settings.host,
    port: settings.port,
    security: settings.security,
    userName: settings.userName ?? '',
    password: '',
    fromName: settings.fromName,
    fromAddress: settings.fromAddress,
    siteUrl: settings.configured ? settings.siteUrl : window.location.origin,
  })
}

onMounted(load)

function payload(): EmailSettingsEditDto {
  return { ...form, userName: form.userName?.trim() || null, password: form.password || null }
}

async function save() {
  saving.value = true
  message.value = null
  try {
    await updateEmailSettings(payload())
    message.value = { text: 'Saved.', error: false }
    await load()
  } catch (e) {
    message.value = { text: apiErrorMessage(e, 'Saving failed.'), error: true }
  } finally {
    saving.value = false
  }
}

async function sendTest() {
  testing.value = true
  message.value = null
  try {
    const result = await testEmailSettings({ ...payload(), to: testTo.value })
    message.value = { text: result.message, error: !result.success }
  } catch (e) {
    message.value = { text: apiErrorMessage(e, 'The test couldn’t run.'), error: true }
  } finally {
    testing.value = false
  }
}
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4">
      <h1 class="text-h5">Settings</h1>
    </div>

    <v-card v-if="email" variant="outlined" class="pa-4 settings-card">
      <div class="d-flex align-center mb-4">
        <h2 class="text-subtitle-1 font-weight-medium">Email</h2>
        <v-chip v-if="!email.configured" size="x-small" variant="tonal" class="ml-2">
          Not set up
        </v-chip>
        <v-spacer />
        <v-switch
          v-model="form.enabled"
          label="Enabled"
          color="primary"
          density="compact"
          hide-details
        />
      </div>

      <div class="form-grid">
        <v-text-field
          v-model="form.host"
          label="SMTP host"
          placeholder="smtp.example.com"
          class="wide"
        />
        <v-text-field v-model.number="form.port" type="number" min="1" max="65535" label="Port" />
        <v-select v-model="form.security" :items="securityOptions" label="Security" />
        <v-text-field v-model="form.userName" label="Username" autocomplete="off" />
        <v-text-field
          v-model="form.password"
          label="Password"
          type="password"
          autocomplete="new-password"
          :placeholder="email.hasPassword ? 'Unchanged' : undefined"
        />
        <v-text-field v-model="form.fromName" label="From name" />
        <v-text-field v-model="form.fromAddress" label="From address" type="email" />
        <v-text-field
          v-model="form.siteUrl"
          label="Site URL"
          placeholder="https://track.example.com"
          class="wide"
        />
      </div>

      <div class="d-flex align-center ga-3">
        <v-text-field
          v-model="testTo"
          label="Send test to"
          type="email"
          density="compact"
          hide-details
        />
        <v-btn variant="tonal" :loading="testing" :disabled="!testTo.trim()" @click="sendTest">
          Send test
        </v-btn>
      </div>

      <v-alert
        v-if="message"
        :type="message.error ? 'error' : 'success'"
        variant="tonal"
        density="compact"
        class="mt-4"
      >
        {{ message.text }}
      </v-alert>

      <div class="d-flex mt-4">
        <v-spacer />
        <v-btn color="primary" variant="flat" :loading="saving" @click="save">Save</v-btn>
      </div>
    </v-card>
  </div>
</template>

<style scoped>
.settings-card {
  max-width: 720px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  column-gap: 12px;
}

.form-grid .wide {
  grid-column: 1 / -1;
}

@media (max-width: 600px) {
  .form-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}
</style>
