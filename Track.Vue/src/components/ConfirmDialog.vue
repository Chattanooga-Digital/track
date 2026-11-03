<script setup lang="ts">
const open = defineModel<boolean>({ required: true })

defineProps<{
  title: string
  confirmLabel?: string
  busy?: boolean
  error?: string
}>()

const emit = defineEmits<{ confirm: [] }>()
</script>

<template>
  <v-dialog v-model="open" max-width="440" :persistent="busy">
    <v-card>
      <v-card-title class="text-subtitle-1">{{ title }}</v-card-title>
      <v-card-text>
        <slot />
        <v-alert v-if="error" type="error" variant="tonal" density="compact" class="mt-3">
          {{ error }}
        </v-alert>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn variant="text" :disabled="busy" @click="open = false">Cancel</v-btn>
        <v-btn color="error" variant="flat" :loading="busy" @click="emit('confirm')">
          {{ confirmLabel ?? 'Delete' }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
