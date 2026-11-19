// Mirror Track.Enums.
export type BackupVolumeStatus =
  | 'Ok'
  | 'Queued'
  | 'Running'
  | 'Stale'
  | 'Failed'
  | 'Never'
  | 'Excluded'
  | 'StaleCopy'
  | 'Ambiguous'
  | 'NodeOffline'
export type BackupJobKind =
  | 'Backup'
  | 'Restore'
  | 'Hook'
  | 'Scale'
  | 'Prune'
  | 'Check'
  | 'Replace'
  | 'Import'
  | 'Verify'
export type BackupJobStatus =
  | 'Queued'
  | 'Dispatched'
  | 'Running'
  | 'Succeeded'
  | 'Failed'
  | 'Cancelled'
export type BackupTrigger = 'Schedule' | 'Manual' | 'Restore'
export type ExclusionSource = 'Label' | 'Track' | 'Empty'
export type BackupMethod = 'Files' | 'Dump'
export type DatabaseEngine = 'Postgres' | 'MariaDb' | 'MySql'
export type S3Addressing = 'Auto' | 'VirtualHost' | 'Path'
export type RestoreMode = 'NewVolume' | 'Replace'
export type DayOfWeek =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday'

export interface BackupVolumeDto {
  key: string
  volumeName: string
  nodeName: string | null
  stackName: string | null
  services: string[]
  images: string[]
  copies: string[]
  sizeBytes: number | null
  status: BackupVolumeStatus
  excludedBy: ExclusionSource | null
  override: boolean | null
  hasHooks: boolean
  hasRestoreHooks: boolean
  databaseWithoutDump: boolean
  method: BackupMethod
  methodOverride: BackupMethod | null
  engine: DatabaseEngine | null
  lastTest: BackupTestDto | null
  paths: string[]
  lastGoodAt: string | null
  activeJobId: number | null
  activeJobKind: BackupJobKind | null
  percentDone: number | null
  lastError: string | null
  replaceBlockedReason: string | null
  dumpReplaceBlockedReason: string | null
}

export interface BackupTestDto {
  jobId: number
  finishedAt: string | null
  succeeded: boolean
  message: string | null
}

export interface AnonymousDatabaseDto {
  nodeName: string
  serviceName: string
  stackName: string | null
}

export interface BackupGroupDto {
  customerId: number | null
  customerName: string | null
  isShared: boolean
  repositoryKey: string
  repositorySizeBytes: number | null
  volumes: BackupVolumeDto[]
}

export interface BackupOverviewDto {
  targetSet: boolean
  masterKeySet: boolean
  paused: boolean
  included: number
  protected: number
  needsAttention: number
  ignored: number
  uploadedLast24HoursBytes: number
  readyNodes: string[]
  groups: BackupGroupDto[]
  anonymousDatabases: AnonymousDatabaseDto[]
}

export interface BackupJobDto {
  id: number
  kind: BackupJobKind
  status: BackupJobStatus
  trigger: BackupTrigger
  volumeKey: string | null
  volumeName: string | null
  nodeName: string | null
  repositoryKey: string | null
  parentJobId: number | null
  stepName: string | null
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
  percentDone: number | null
  bytesDone: number | null
  bytesTotal: number | null
  bytesAdded: number | null
  filesNew: number | null
  filesChanged: number | null
  snapshotId: string | null
  error: string | null
  summary: string | null
  requestedBy: string | null
  cancelRequested: boolean
  output: string | null
}

export interface BackupSnapshotDto {
  id: number
  snapshotId: string
  repositoryKey: string
  volumeKey: string
  volumeName: string
  time: string
  nodeName: string | null
  images: string[]
  paths: string[]
  bytesAdded: number | null
  totalBytes: number | null
  preRestore: boolean
  dumpEngine: DatabaseEngine | null
  differentImage: boolean
}

export interface SnapshotEntryDto {
  name: string
  isDirectory: boolean
  size: number | null
  modifiedAt: string | null
}

export interface SnapshotListingDto {
  path: string
  entries: SnapshotEntryDto[]
}

export interface BackupSettingsDto {
  paused: boolean
  backupMinuteUtc: number
  spreadMinutes: number
  maxJobsPerNode: number
  maxJobsTotal: number
  keepDaily: number
  keepWeekly: number
  keepMonthly: number
  pruneDay: DayOfWeek
  pruneMinuteUtc: number
  checkPercent: number
  staleAfterHours: number
  hookTimeoutMinutes: number
  heartbeatUrl: string | null
}

export interface BackupRepositoryDto {
  key: string
  customerName: string | null
  isShared: boolean
  sizeBytes: number | null
  lastPrunedAt: string | null
  lastCheckedAt: string | null
  lastMaintenanceError: string | null
}

export interface BackupTargetDto {
  configured: boolean
  endpoint: string
  region: string
  bucket: string
  prefix: string
  addressing: S3Addressing
  agentAccessKey: string
  hasAgentSecret: boolean
  maintenanceAccessKey: string | null
  hasMaintenanceSecret: boolean
  masterKeySet: boolean
  masterKeyFromEnvironment: boolean
  masterKeySecretName: string
  repositories: BackupRepositoryDto[]
}

export interface BackupTargetEditDto {
  endpoint: string
  region: string
  bucket: string
  prefix: string
  addressing: S3Addressing
  agentAccessKey: string
  agentSecretKey: string | null
  maintenanceAccessKey: string | null
  maintenanceSecretKey: string | null
}

export interface BackupTargetCheckDto {
  label: string
  ok: boolean
  detail: string | null
}

export interface RestoreRequestDto {
  snapshotId: number
  mode: RestoreMode
  nodeName: string | null
  confirmVolumeName: string | null
}
