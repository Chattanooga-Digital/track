<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { forgotPassword, getAuthOptions } from '@/api/authApi'
import { apiErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/authStore'

const authStore = useAuthStore()

const username = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)

const mode = ref<'signIn' | 'forgot' | 'sent'>('signIn')
const passwordReset = ref(false)
const forgotLogin = ref('')

onMounted(async () => {
  passwordReset.value = (await getAuthOptions()).passwordReset
})

function showForgot() {
  forgotLogin.value = username.value
  error.value = ''
  mode.value = 'forgot'
}

function showSignIn() {
  error.value = ''
  mode.value = 'signIn'
}

async function sendLink() {
  error.value = ''
  submitting.value = true
  try {
    await forgotPassword(forgotLogin.value)
    mode.value = 'sent'
  } catch (e) {
    error.value = apiErrorMessage(e, 'Sending the link failed. Please try again.')
  } finally {
    submitting.value = false
  }
}

async function submit() {
  error.value = ''
  submitting.value = true
  try {
    await authStore.login(username.value, password.value)
  } catch (e) {
    if (
      typeof e === 'object' &&
      e !== null &&
      'response' in e &&
      (e as { response?: { status?: number } }).response?.status === 401
    ) {
      error.value = 'Invalid username or password.'
    } else {
      error.value = 'Login failed. Please try again.'
    }
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
            <div class="text-h6 mt-2">Track</div>
            <div class="text-caption text-medium-emphasis">Docker Swarm monitoring</div>
          </v-card-title>
          <v-card-text>
            <template v-if="mode === 'sent'">
              <v-alert type="success" density="compact" variant="tonal" class="mb-3">
                If the account has an email address, a link is on its way.
              </v-alert>
              <v-btn variant="text" size="small" block @click="showSignIn">Back to sign in</v-btn>
            </template>

            <v-form v-else-if="mode === 'forgot'" @submit.prevent="sendLink">
              <v-text-field
                v-model="forgotLogin"
                label="Username or email"
                prepend-inner-icon="mdi-account"
                autofocus
                autocomplete="username"
              />
              <v-alert v-if="error" type="error" density="compact" variant="tonal" class="mb-3">
                {{ error }}
              </v-alert>
              <v-btn
                type="submit"
                color="primary"
                block
                :loading="submitting"
                :disabled="!forgotLogin.trim()"
              >
                Send Link
              </v-btn>
              <v-btn variant="text" size="small" block class="mt-2" @click="showSignIn">
                Back to sign in
              </v-btn>
            </v-form>

            <v-form v-else @submit.prevent="submit">
              <v-text-field
                v-model="username"
                label="Username"
                prepend-inner-icon="mdi-account"
                autofocus
                autocomplete="username"
              />
              <v-text-field
                v-model="password"
                label="Password"
                type="password"
                prepend-inner-icon="mdi-lock"
                autocomplete="current-password"
              />
              <v-alert v-if="error" type="error" density="compact" variant="tonal" class="mb-3">
                {{ error }}
              </v-alert>
              <v-btn
                type="submit"
                color="primary"
                block
                :loading="submitting"
                :disabled="!username || !password"
              >
                Sign In
              </v-btn>
              <v-btn
                v-if="passwordReset"
                variant="text"
                size="small"
                block
                class="mt-2"
                @click="showForgot"
              >
                Forgot password?
              </v-btn>
            </v-form>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </v-container>
</template>
