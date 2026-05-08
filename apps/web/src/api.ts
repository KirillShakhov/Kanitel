import type { BootstrapPayload } from './types'

const authTokenKey = 'kanitel-auth-token'

export function readAuthToken() {
  return window.localStorage.getItem(authTokenKey) ?? ''
}

export function storeAuthToken(token: string) {
  if (token) {
    window.localStorage.setItem(authTokenKey, token)
    return
  }

  window.localStorage.removeItem(authTokenKey)
}

function jsonHeaders() {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json'
  }
  const token = readAuthToken()
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }
  return headers
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
  return readJson<BootstrapPayload>(await fetch('/api/bootstrap', { headers: jsonHeaders() }))
}

export async function postJson<T>(url: string, body: unknown): Promise<T> {
  return readJson<T>(
    await fetch(url, {
      method: 'POST',
      headers: jsonHeaders(),
      body: JSON.stringify(body)
    })
  )
}

export async function patchJson<T>(url: string, body: unknown): Promise<T> {
  return readJson<T>(
    await fetch(url, {
      method: 'PATCH',
      headers: jsonHeaders(),
      body: JSON.stringify(body)
    })
  )
}

export async function deleteJson(url: string): Promise<void> {
  await readJson<void>(
    await fetch(url, {
      method: 'DELETE',
      headers: jsonHeaders()
    })
  )
}
