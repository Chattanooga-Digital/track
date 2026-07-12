<script setup lang="ts">
import { ref, watch } from 'vue'
import { changePassword } from '@/api/authApi'
import { apiErrorMessage } from '@/api/http'

const open = defineModel<boolean>({ required: true })

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const saving = ref(false)
const error = ref('')
const done = ref(false)

watch(open, (isOpen) => {
  if (!isOpen) return
  currentPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  error.value = ''
  done.value = false
})

async function save() {
  saving.value = true
  error.value = ''
  try {
    await changePassword(currentPassword.value, newPassword.value)
    done.value = true
  } catch (e) {
    error.value = apiErrorMessage(e, 'Changing the password failed.')
  } finally {
    saving.value = false
  }
}

const canSave = () =>
  currentPassword.value.length > 0 &&
  newPassword.value.length > 0 &&
  newPassword.value === confirmPassword.value
</script>

<template>
  <v-dialog v-model="open" max-width="480">
    <v-card>
      <v-card-title>Change Password</v-card-title>
      <v-card-text>
        <template v-if="done">
          <v-alert type="success" variant="tonal" density="compact">
            Your password has been changed.
          </v-alert>
        </template>
        <template v-else>
          <v-text-field
            v-model="currentPassword"
            label="Current password"
            type="password"
            autocomplete="current-password"
            class="mb-2"
          />
          <v-text-field
            v-model="newPassword"
            label="New password"
            type="password"
            autocomplete="new-password"
            class="mb-2"
          />
          <v-text-field
            v-model="confirmPassword"
            label="Confirm new password"
            type="password"
            autocomplete="new-password"
            :error-messages="
              confirmPassword && confirmPassword !== newPassword ? 'Passwords do not match.' : []
            "
          />

          <v-alert v-if="error" type="error" variant="tonal" density="compact" class="mt-3">
            {{ error }}
          </v-alert>
        </template>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <template v-if="done">
          <v-btn color="primary" @click="open = false">Close</v-btn>
        </template>
        <template v-else>
          <v-btn variant="text" @click="open = false">Cancel</v-btn>
          <v-btn color="primary" :loading="saving" :disabled="!canSave()" @click="save">
            Change Password
          </v-btn>
        </template>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
