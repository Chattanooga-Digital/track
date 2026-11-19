<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { apiErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/authStore'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()

const userId = computed(() => String(route.query.user ?? ''))
const token = computed(() => String(route.query.token ?? ''))
const linkComplete = computed(() => userId.value.length > 0 && token.value.length > 0)

const password = ref('')
const confirmPassword = ref('')
const error = ref('')
const submitting = ref(false)

const canSubmit = computed(
  () => password.value.length > 0 && password.value === confirmPassword.value,
)

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    await authStore.setPasswordAndSignIn(userId.value, token.value, password.value)
    await router.replace('/')
  } catch (e) {
    error.value = apiErrorMessage(e, 'Setting the password failed.')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <v-container class="fill-height" fluid>
    <v-row justify="center" align="center">
      <v-col cols="12" sm="8" md="4">
        <v-card variant="outlined">
          <v-card-title class="text-center pt-6">
            <img src="/logo.svg" alt="Track" height="56" />
            <div class="text-h6 mt-2">Set your password</div>
          </v-card-title>
          <v-card-text>
            <v-alert
              v-if="!linkComplete"
              type="error"
              density="compact"
              variant="tonal"
              class="mb-3"
            >
              This link is incomplete. Copy the whole link from the email.
            </v-alert>
            <v-form v-else @submit.prevent="submit">
              <v-text-field
                v-model="password"
                label="New password"
                type="password"
                prepend-inner-icon="mdi-lock"
                autofocus
                autocomplete="new-password"
              />
              <v-text-field
                v-model="confirmPassword"
                label="Confirm password"
                type="password"
                prepend-inner-icon="mdi-lock-check"
                autocomplete="new-password"
                :error-messages="
                  confirmPassword && confirmPassword !== password ? 'Passwords do not match.' : []
                "
              />
              <v-alert v-if="error" type="error" density="compact" variant="tonal" class="mb-3">
                {{ error }}
              </v-alert>
              <v-btn
                type="submit"
                color="primary"
                block
                :loading="submitting"
                :disabled="!canSubmit"
              >
                Set Password
              </v-btn>
            </v-form>
            <v-btn variant="text" size="small" block class="mt-2" @click="router.replace('/')">
              Back to sign in
            </v-btn>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>
