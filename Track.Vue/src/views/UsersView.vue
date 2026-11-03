<script setup lang="ts">
import { ref, onMounted } from 'vue'
import type { UserDto } from '@/types/users'
import { getUsers, createUser, updateUser, setUserPassword, deleteUser } from '@/api/usersApi'
import { apiErrorMessage } from '@/api/http'
import { useAuthStore } from '@/stores/authStore'

const authStore = useAuthStore()

const users = ref<UserDto[]>([])
const loading = ref(true)

const dialog = ref(false)
const editing = ref<UserDto | null>(null)
const form = ref(blankForm())
const saving = ref(false)
const dialogError = ref('')

const passwordDialog = ref(false)
const passwordTarget = ref<UserDto | null>(null)
const newPassword = ref('')
const confirmPassword = ref('')
const settingPassword = ref(false)
const passwordError = ref('')

function blankForm() {
  return {
    username: '',
    password: '',
    email: '',
    displayName: '',
    isAdministrator: false,
    disabled: false,
  }
}

async function refresh() {
  loading.value = true
  try {
    users.value = await getUsers()
  } finally {
    loading.value = false
  }
}

onMounted(refresh)

const isSelf = (user: UserDto) => user.username === authStore.user?.username

function openCreate() {
  editing.value = null
  form.value = blankForm()
  dialogError.value = ''
  dialog.value = true
}

function openEdit(user: UserDto) {
  editing.value = user
  form.value = {
    username: user.username,
    password: '',
    email: user.email ?? '',
    displayName: user.displayName ?? '',
    isAdministrator: user.isAdministrator,
    disabled: user.disabled,
  }
  dialogError.value = ''
  dialog.value = true
}

async function save() {
  saving.value = true
  dialogError.value = ''
  try {
    if (editing.value) {
      await updateUser(editing.value.id, {
        email: form.value.email || null,
        displayName: form.value.displayName || null,
        isAdministrator: form.value.isAdministrator,
        disabled: form.value.disabled,
      })
    } else {
      await createUser({
        username: form.value.username,
        password: form.value.password,
        email: form.value.email || null,
        displayName: form.value.displayName || null,
        isAdministrator: form.value.isAdministrator,
      })
    }
    dialog.value = false
    await refresh()
  } catch (e) {
    dialogError.value = apiErrorMessage(e, 'Saving the user failed.')
  } finally {
    saving.value = false
  }
}

function openSetPassword(user: UserDto) {
  passwordTarget.value = user
  newPassword.value = ''
  confirmPassword.value = ''
  passwordError.value = ''
  passwordDialog.value = true
}

async function savePassword() {
  if (!passwordTarget.value) return
  settingPassword.value = true
  passwordError.value = ''
  try {
    await setUserPassword(passwordTarget.value.id, newPassword.value)
    passwordDialog.value = false
  } catch (e) {
    passwordError.value = apiErrorMessage(e, 'Setting the password failed.')
  } finally {
    settingPassword.value = false
  }
}

async function remove(user: UserDto) {
  if (!confirm(`Delete user "${user.username}"? This cannot be undone.`)) return
  try {
    await deleteUser(user.id)
  } catch (e) {
    alert(apiErrorMessage(e, 'Deleting the user failed.'))
  }
  await refresh()
}

const canSave = () =>
  editing.value !== null ||
  (form.value.username.trim().length > 0 && form.value.password.length > 0)

const canSavePassword = () =>
  newPassword.value.length > 0 && newPassword.value === confirmPassword.value
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4">
      <h1 class="text-h5">Users</h1>
      <v-spacer />
      <v-btn color="primary" prepend-icon="mdi-account-plus" @click="openCreate">New User</v-btn>
    </div>

    <v-progress-linear v-if="loading" indeterminate />

    <v-card variant="outlined">
      <v-table>
        <thead>
          <tr>
            <th>Username</th>
            <th>Display name</th>
            <th>Email</th>
            <th>Role</th>
            <th>Status</th>
            <th>Created</th>
            <th class="text-right">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="user in users" :key="user.id">
            <td class="font-weight-medium">
              {{ user.username }}
              <v-chip v-if="isSelf(user)" size="x-small" variant="tonal" class="ml-1">You</v-chip>
            </td>
            <td class="text-medium-emphasis">{{ user.displayName ?? '—' }}</td>
            <td class="text-medium-emphasis">{{ user.email ?? '—' }}</td>
            <td>
              <v-chip v-if="user.isAdministrator" size="x-small" color="primary" variant="tonal">
                Administrator
              </v-chip>
            </td>
            <td>
              <v-chip
                size="x-small"
                :color="user.disabled ? 'error' : user.lockedOut ? 'warning' : 'success'"
                variant="tonal"
              >
                {{ user.disabled ? 'Disabled' : user.lockedOut ? 'Locked out' : 'Active' }}
              </v-chip>
            </td>
            <td class="text-medium-emphasis">
              {{ new Date(user.createdAt).toLocaleDateString() }}
            </td>
            <td class="text-right text-no-wrap">
              <v-btn size="small" variant="text" icon="mdi-pencil" @click="openEdit(user)" />
              <v-btn size="small" variant="text" icon="mdi-key" @click="openSetPassword(user)" />
              <v-btn
                size="small"
                variant="text"
                icon="mdi-delete"
                color="error"
                :disabled="isSelf(user)"
                @click="remove(user)"
              />
            </td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <v-dialog v-model="dialog" max-width="560">
      <v-card>
        <v-card-title>{{ editing ? 'Edit User' : 'New User' }}</v-card-title>
        <v-card-text>
          <v-text-field
            v-model="form.username"
            label="Username"
            :disabled="editing !== null"
            autocomplete="off"
            class="mb-2"
          />
          <v-text-field
            v-if="!editing"
            v-model="form.password"
            label="Password"
            type="password"
            autocomplete="new-password"
            class="mb-2"
          />
          <v-text-field v-model="form.displayName" label="Display name" class="mb-2" />
          <v-text-field v-model="form.email" label="Email" type="email" class="mb-2" />
          <v-switch
            v-model="form.isAdministrator"
            label="Administrator"
            color="primary"
            :disabled="editing !== null && isSelf(editing)"
            hide-details
          />
          <v-switch
            v-if="editing"
            v-model="form.disabled"
            label="Disabled"
            color="error"
            :disabled="isSelf(editing)"
            hide-details
          />

          <v-alert v-if="dialogError" type="error" variant="tonal" density="compact" class="mt-3">
            {{ dialogError }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="dialog = false">Cancel</v-btn>
          <v-btn color="primary" :loading="saving" :disabled="!canSave()" @click="save">Save</v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-dialog v-model="passwordDialog" max-width="480">
      <v-card>
        <v-card-title>Set Password for {{ passwordTarget?.username }}</v-card-title>
        <v-card-text>
          <v-text-field
            v-model="newPassword"
            label="New password"
            type="password"
            autocomplete="new-password"
            class="mb-2"
          />
          <v-text-field
            v-model="confirmPassword"
            label="Confirm password"
            type="password"
            autocomplete="new-password"
            :error-messages="
              confirmPassword && confirmPassword !== newPassword ? 'Passwords do not match.' : []
            "
          />
          <div class="text-caption text-medium-emphasis mt-1">
            Setting a password signs the user out of all sessions.
          </div>

          <v-alert v-if="passwordError" type="error" variant="tonal" density="compact" class="mt-3">
            {{ passwordError }}
          </v-alert>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn variant="text" @click="passwordDialog = false">Cancel</v-btn>
          <v-btn
            color="primary"
            :loading="settingPassword"
            :disabled="!canSavePassword()"
            @click="savePassword"
          >
            Set Password
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>
  </div>
</template>
