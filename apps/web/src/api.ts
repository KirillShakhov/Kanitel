import type { BootstrapPayload } from './types'

const jsonHeaders = {
  'Content-Type': 'application/json'
}

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const text = await response.text()
    throw new Error(text || response.statusText)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

export async function loadBootstrap(): Promise<BootstrapPayload> {
  return readJson<BootstrapPayload>(await fetch('/api/bootstrap'))
}

export async function postJson<T>(url: string, body: unknown): Promise<T> {
  return readJson<T>(
    await fetch(url, {
      method: 'POST',
      headers: jsonHeaders,
      body: JSON.stringify(body)
    })
  )
}

export async function patchJson<T>(url: string, body: unknown): Promise<T> {
  return readJson<T>(
    await fetch(url, {
      method: 'PATCH',
      headers: jsonHeaders,
      body: JSON.stringify(body)
    })
  )
}

export async function deleteJson(url: string): Promise<void> {
  await readJson<void>(
    await fetch(url, {
      method: 'DELETE'
    })
  )
}
