import { http } from './http'
import type {
  BackupJobDto,
  BackupOverviewDto,
  BackupSettingsDto,
  BackupSnapshotDto,
  BackupTargetCheckDto,
  BackupTargetDto,
  BackupTargetEditDto,
  BackupMethod,
  RestoreRequestDto,
  SnapshotListingDto,
} from '@/types/backups'

export async function getBackupOverview(): Promise<BackupOverviewDto> {
  const { data } = await http.get<BackupOverviewDto>('/backups')
  return data
}

export async function setVolumeOverride(
  volumeNames: string[],
  excluded: boolean | null,
): Promise<void> {
  await http.put('/backups/volumes/override', { volumeNames, excluded })
}

export async function setVolumeMethod(
  volumeName: string,
  method: BackupMethod | null,
): Promise<void> {
  await http.put('/backups/volumes/method', { volumeName, method })
}

export async function testRestore(key: string, snapshotId: number | null = null): Promise<void> {
  await http.post('/backups/volumes/test', { key, snapshotId })
}

export async function backUpVolume(key: string): Promise<void> {
  await http.post('/backups/volumes/backup', { key })
}

export async function backUpAll(): Promise<void> {
  await http.post('/backups/volumes/backup-all')
}

export async function getSnapshots(volume: string): Promise<BackupSnapshotDto[]> {
  const { data } = await http.get<BackupSnapshotDto[]>('/backups/snapshots', { params: { volume } })
  return data
}

export async function getSnapshotFiles(id: number, path: string): Promise<SnapshotListingDto> {
  const { data } = await http.get<SnapshotListingDto>(`/backups/snapshots/${id}/files`, {
    params: { path },
  })
  return data
}

export function snapshotDownloadUrl(id: number, path = '', directory = false): string {
  const query = new URLSearchParams()
  if (path) query.set('path', path)
  if (directory) query.set('directory', 'true')
  const suffix = query.size ? `?${query}` : ''
  return `/api/v0/backups/snapshots/${id}/download${suffix}`
}

export async function restoreSnapshot(request: RestoreRequestDto): Promise<void> {
  await http.post('/backups/restore', request)
}

export async function getBackupJobs(): Promise<BackupJobDto[]> {
  const { data } = await http.get<BackupJobDto[]>('/backups/jobs')
  return data
}

export async function getBackupJob(id: number): Promise<BackupJobDto> {
  const { data } = await http.get<BackupJobDto>(`/backups/jobs/${id}`)
  return data
}

export async function cancelBackupJob(id: number): Promise<void> {
  await http.post(`/backups/jobs/${id}/cancel`)
}

export async function getBackupSettings(): Promise<BackupSettingsDto> {
  const { data } = await http.get<BackupSettingsDto>('/backups/settings')
  return data
}

export async function updateBackupSettings(settings: BackupSettingsDto): Promise<void> {
  await http.put('/backups/settings', settings)
}

export async function getBackupTarget(): Promise<BackupTargetDto> {
  const { data } = await http.get<BackupTargetDto>('/backups/target')
  return data
}

export async function updateBackupTarget(target: BackupTargetEditDto): Promise<void> {
  await http.put('/backups/target', target)
}

export async function testBackupTarget(
  target: BackupTargetEditDto,
): Promise<BackupTargetCheckDto[]> {
  const { data } = await http.post<BackupTargetCheckDto[]>('/backups/target/test', target)
  return data
}

export async function runBackupMaintenance(): Promise<void> {
  await http.post('/backups/maintenance')
}
