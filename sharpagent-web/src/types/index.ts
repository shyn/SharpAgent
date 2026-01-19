export type MessageRole = 'user' | 'assistant' | 'system'

export interface ToolCallData {
  id: string
  name: string
  state: 'input-streaming' | 'input-available' | 'output-available' | 'output-error'
  input?: Record<string, unknown>
  output?: Record<string, unknown>
  errorText?: string
}

export interface ChatMessage {
  id: string
  role: MessageRole
  content: string
  timestamp: Date
  toolCalls?: ToolCallData[]
  reasoning?: string
  isStreaming?: boolean
}

export type ThinkingLevel = 'off' | 'low' | 'medium' | 'high'

export type Provider = 'openai' | 'anthropic'

export interface ModelConfig {
  provider: Provider
  model: string
  apiKey: string
}

export const PROVIDER_MODELS: Record<Provider, string[]> = {
  openai: ['gpt-4o', 'gpt-4o-mini', 'gpt-4-turbo', 'gpt-3.5-turbo', 'o1', 'o1-mini', 'o1-pro', 'o3', 'o3-mini', 'o4-mini'],
  anthropic: ['claude-sonnet-4-20250514', 'claude-3-5-sonnet-20241022', 'claude-3-5-haiku-20241022', 'claude-3-opus-20240229'],
}
