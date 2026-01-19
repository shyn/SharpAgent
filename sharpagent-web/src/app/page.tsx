"use client"

import { useState, useCallback, useRef, useEffect } from "react"
import { ChatMessage, ThinkingLevel, ToolCallData, ModelConfig, PROVIDER_MODELS } from "@/types"
import { ChatHeader } from "@/components/chat/chat-header"
import { ChatMessages } from "@/components/chat/chat-messages"
import { ChatInput } from "@/components/chat/chat-input"
import { streamChat } from "@/lib/api"

const STORAGE_KEY = "sharpagent-model-config"

function loadModelConfig(): ModelConfig {
  if (typeof window === "undefined") {
    return { provider: "openai", model: "gpt-4o-mini", apiKey: "" }
  }
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved) {
      const parsed = JSON.parse(saved)
      if (parsed.provider && parsed.model) {
        return {
          provider: parsed.provider,
          model: parsed.model,
          apiKey: parsed.apiKey || "",
        }
      }
    }
  } catch {}
  return { provider: "openai", model: "gpt-4o-mini", apiKey: "" }
}

function saveModelConfig(config: ModelConfig) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(config))
  } catch {}
}

export default function Home() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [isLoading, setIsLoading] = useState(false)
  const [thinkingLevel, setThinkingLevel] = useState<ThinkingLevel>("off")
  const [modelConfig, setModelConfig] = useState<ModelConfig>(() => loadModelConfig())
  const abortControllerRef = useRef<AbortController | null>(null)

  useEffect(() => {
    setModelConfig(loadModelConfig())
  }, [])

  const handleModelConfigChange = useCallback((config: ModelConfig) => {
    setModelConfig(config)
    saveModelConfig(config)
  }, [])

  const handleSend = useCallback(async (content: string) => {
    const userMessage: ChatMessage = {
      id: crypto.randomUUID(),
      role: "user",
      content,
      timestamp: new Date(),
    }
    setMessages((prev) => [...prev, userMessage])
    setIsLoading(true)

    const assistantId = crypto.randomUUID()
    let assistantMessage: ChatMessage = {
      id: assistantId,
      role: "assistant",
      content: "",
      timestamp: new Date(),
      isStreaming: true,
      toolCalls: [],
      reasoning: undefined,
    }
    setMessages((prev) => [...prev, assistantMessage])

    abortControllerRef.current = new AbortController()
    const toolCallsMap = new Map<string, ToolCallData>()
    const toolArgsMap = new Map<string, string>()
    let thinkingBuffer = ""

    try {
      const chatConfig = {
        provider: modelConfig.provider,
        model: modelConfig.model,
        apiKey: modelConfig.apiKey || undefined,
      }

      for await (const event of streamChat(
        content, 
        thinkingLevel, 
        chatConfig,
        abortControllerRef.current.signal
      )) {
        const update = (partial: Partial<ChatMessage>) => {
          assistantMessage = { ...assistantMessage, ...partial }
          setMessages((prev) =>
            prev.map((m) => (m.id === assistantId ? assistantMessage : m))
          )
        }

        switch (event.type) {
          case "text_delta":
            update({ content: assistantMessage.content + (event.data?.text || "") })
            break

          case "thinking_delta":
            thinkingBuffer += event.data?.thinking || ""
            update({ reasoning: thinkingBuffer })
            break

          case "thinking_completed":
            update({ reasoning: (event.data?.fullThinking as string) || thinkingBuffer })
            break

          case "tool_use_started": {
            const id = event.data?.id as string
            const name = event.data?.name as string
            toolCallsMap.set(id, {
              id,
              name,
              state: "input-streaming",
            })
            toolArgsMap.set(id, "")
            update({ toolCalls: Array.from(toolCallsMap.values()) })
            break
          }

          case "tool_use_args_delta": {
            const id = event.data?.id as string
            const partial = event.data?.partialJson as string
            const current = toolArgsMap.get(id) || ""
            toolArgsMap.set(id, current + partial)
            break
          }

          case "tool_use_completed": {
            const id = event.data?.id as string
            const tool = toolCallsMap.get(id)
            if (tool) {
              const argsJson = toolArgsMap.get(id) || "{}"
              let input: Record<string, unknown> = {}
              try {
                input = JSON.parse(argsJson)
              } catch {
                input = { raw: argsJson }
              }
              toolCallsMap.set(id, { ...tool, state: "input-available", input })
              update({ toolCalls: Array.from(toolCallsMap.values()) })
            }
            break
          }

          case "tool_call_started": {
            const id = event.data?.id as string
            const name = event.data?.name as string
            const args = event.data?.arguments as string
            let input: Record<string, unknown> = {}
            try {
              input = JSON.parse(args || "{}")
            } catch {
              input = { raw: args }
            }
            toolCallsMap.set(id, {
              id,
              name,
              state: "input-available",
              input,
            })
            update({ toolCalls: Array.from(toolCallsMap.values()) })
            break
          }

          case "tool_call_completed": {
            const id = event.data?.id as string
            const result = event.data?.result as string
            const isError = event.data?.isError as boolean
            const tool = toolCallsMap.get(id)
            if (tool) {
              let output: Record<string, unknown> = {}
              try {
                output = JSON.parse(result || "{}")
              } catch {
                output = { content: result }
              }
              toolCallsMap.set(id, {
                ...tool,
                state: isError ? "output-error" : "output-available",
                output: isError ? undefined : output,
                errorText: isError ? result : undefined,
              })
              update({ toolCalls: Array.from(toolCallsMap.values()) })
            }
            break
          }

          case "completed":
            update({
              content: (event.data?.finalAnswer as string) || assistantMessage.content,
              isStreaming: false,
            })
            break

          case "error":
            update({
              content: assistantMessage.content || `Error: ${event.data?.message}`,
              isStreaming: false,
            })
            break
        }
      }
    } catch (err) {
      if (err instanceof Error && err.name !== "AbortError") {
        setMessages((prev) =>
          prev.map((m) =>
            m.id === assistantId
              ? { ...m, content: `Error: ${err.message}`, isStreaming: false }
              : m
          )
        )
      }
    } finally {
      setIsLoading(false)
      abortControllerRef.current = null
      setMessages((prev) =>
        prev.map((m) =>
          m.id === assistantId ? { ...m, isStreaming: false } : m
        )
      )
    }
  }, [thinkingLevel, modelConfig])

  const handleStop = useCallback(() => {
    abortControllerRef.current?.abort()
    setIsLoading(false)
  }, [])

  const handleClear = useCallback(() => {
    setMessages([])
  }, [])

  return (
    <div className="flex h-screen flex-col bg-background">
      <ChatHeader 
        onClear={handleClear} 
        canClear={messages.length > 0 && !isLoading}
        modelConfig={modelConfig}
        onModelConfigChange={handleModelConfigChange}
      />
      <ChatMessages 
        messages={messages} 
        isLoading={isLoading} 
      />
      <ChatInput
        onSend={handleSend}
        onStop={handleStop}
        isLoading={isLoading}
        thinkingLevel={thinkingLevel}
        onThinkingLevelChange={setThinkingLevel}
      />
    </div>
  )
}
