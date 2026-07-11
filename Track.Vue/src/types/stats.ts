export interface ContainerStatsDto {
  nodeName: string
  containerId: string
  containerName: string
  stackName: string | null
  serviceName: string | null
  cpuPercent: number
  memoryUsedBytes: number
  memoryLimitBytes: number
  diskBytes: number | null
  timestamp: string
}

export interface UsageTotalsDto {
  cpuPercent: number
  memoryUsedBytes: number
  memoryLimitBytes: number
  diskBytes: number
  objectStorageBytes: number
  cpuPercentOfCluster: number
  memoryPercentOfCluster: number
  blendedPercentOfCluster: number
}

export interface BucketUsageDto {
  sourceName: string
  bucketName: string
  sizeBytes: number
}

export interface ClusterCapacityDto {
  nodeCount: number
  cpuCores: number
  memoryBytes: number
}

export interface StackGroupDto {
  stackName: string | null
  totals: UsageTotalsDto
  containers: ContainerStatsDto[]
}

export interface CustomerGroupDto {
  customerId: number | null
  customerName: string | null
  totals: UsageTotalsDto
  stacks: StackGroupDto[]
  buckets: BucketUsageDto[]
}

export interface NodeStatusDto {
  name: string
  online: boolean
  error: string | null
  lastSeen: string | null
  containersTracked: number
  unidentifiedContainers: number
  cpuCores: number
  memoryBytes: number
  cpuPercent: number
  memoryUsedBytes: number
}

export interface SnapshotDto {
  timestamp: string
  cluster: ClusterCapacityDto
  totals: UsageTotalsDto
  nodes: NodeStatusDto[]
  customers: CustomerGroupDto[]
}

export interface HistoryBucketDto {
  bucketStart: string
  cpuPercent: number
  memoryUsedBytes: number
  memoryLimitBytes: number
  diskBytes: number | null
  objectStorageBytes: number | null
}
