const API_BASE = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000'

export interface ChatEvent {
  type: string
  data?: Record<string, unknown>
}

export interface ConfigResponse {
  provider: string
  model: string
  hasApiKey: boolean
}

export interface ChatRequestConfig {
  provider?: string
  model?: string
  apiKey?: string
}

export async function getConfig(): Promise<ConfigResponse> {
  const res = await fetch(`${API_BASE}/api/config`)
  if (!res.ok) throw new Error('Failed to fetch config')
  return res.json()
}

export async function* streamChat(
  message: string,
  thinkingLevel: string,
  config?: ChatRequestConfig,
  signal?: AbortSignal
): AsyncGenerator<ChatEvent> {
  const res = await fetch(`${API_BASE}/api/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ 
      message, 
      thinkingLevel,
      provider: config?.provider,
      model: config?.model,
      apiKey: config?.apiKey,
    }),
    signal,
  })

  if (!res.ok) {
    const error = await res.json().catch(() => ({ error: 'Request failed' }))
    throw new Error(error.error || 'Request failed')
  }

  const reader = res.body?.getReader()
  if (!reader) throw new Error('No response body')

  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) break

    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n')
    buffer = lines.pop() || ''

    for (const line of lines) {
      if (line.startsWith('data: ')) {
        const data = line.slice(6).trim()
        if (data === '[DONE]') return
        try {
          yield JSON.parse(data) as ChatEvent
        } catch {
          // ignore parse errors
        }
      }
    }
  }
}
