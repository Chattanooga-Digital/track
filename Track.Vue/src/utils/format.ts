const UNITS = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) return ''
  if (bytes === 0) return '0 B'

  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), UNITS.length - 1)
  const value = bytes / 1024 ** exponent
  return `${value.toFixed(value >= 100 || exponent === 0 ? 0 : 1)} ${UNITS[exponent]}`
}

export function formatCpu(percent: number | null | undefined): string {
  if (percent === null || percent === undefined) return ''
  return `${percent.toFixed(1)}%`
}

export function memoryPercent(used: number, limit: number): number {
  if (limit <= 0) return 0
  return (used / limit) * 100
}

export function formatMoney(amount: number | null | undefined, currency = 'USD'): string {
  if (amount === null || amount === undefined) return ''
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(amount)
  } catch {
    // Unknown/invalid currency code: fall back to a plain number with the code appended.
    return `${amount.toFixed(2)} ${currency}`
  }
}

export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return 'never'
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 5) return 'just now'
  if (seconds < 60) return `${seconds}s ago`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 48) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}

export function absoluteTime(iso: string | null | undefined): string {
  return iso ? new Date(iso).toLocaleString() : ''
}
