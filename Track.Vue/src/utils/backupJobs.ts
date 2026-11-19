import type { BackupJobDto } from '@/types/backups'

export const jobKindLabel: Record<string, string> = {
  Backup: 'Backup',
  Restore: 'Restore',
  Hook: 'Hook',
  Scale: 'Scale',
  Prune: 'Prune',
  Check: 'Check',
  Replace: 'Replace live data',
  Import: 'Import',
  Verify: 'Test restore',
}

export const isJobActive = (job: BackupJobDto) =>
  ['Queued', 'Dispatched', 'Running'].includes(job.status)
