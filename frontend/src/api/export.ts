export type ExportFormat = 'json' | 'csv'

/**
 * URL for the offers export endpoint. The backend streams the file with a
 * Content-Disposition attachment header, so this can back an `<a download href>`.
 */
export function exportUrl(format: ExportFormat): string {
  return `/api/export?format=${format}`
}
