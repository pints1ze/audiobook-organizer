export type ScanStatus = 'Running' | 'Completed' | 'Failed'

export interface ScanRun {
  id: number
  startedAt: string
  finishedAt: string | null
  status: ScanStatus
  filesFound: number
  filesAdded: number
  filesUpdated: number
  filesUnchanged: number
  filesMissing: number
  filesFailed: number
  error: string | null
}

export interface Dashboard {
  incomingPath: string | null
  libraryPath: string | null
  lastScan: ScanRun | null
  audioFileCount: number
  filesWithErrors: number
  bookCount: number
  booksByConfidence: { low: number; medium: number; high: number }
  correctedBookCount: number
  unresolvedFileCount: number
  excludedFileCount: number
}

export type StartScanResult = { started: true; run: ScanRun } | { started: false; message: string }
