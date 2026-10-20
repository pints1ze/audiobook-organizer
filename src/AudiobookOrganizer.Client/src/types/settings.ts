export interface Settings {
  incomingPath: string | null
  libraryPath: string | null
  directoryTemplate: string | null
  fileTemplate: string | null
  keepSourceFiles: boolean
}

export interface SettingsFieldErrors {
  incomingPath?: string[]
  libraryPath?: string[]
  directoryTemplate?: string[]
  fileTemplate?: string[]
}

export type SaveSettingsResult =
  | { ok: true; settings: Settings }
  | { ok: false; errors: SettingsFieldErrors }
