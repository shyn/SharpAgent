"use client"

import { ModelConfig } from "@/types"
import { Button } from "@/components/ui/button"
import { ModelSelector } from "./model-selector"
import { Trash2, Bot } from "lucide-react"

interface ChatHeaderProps {
  onClear: () => void
  canClear: boolean
  modelConfig: ModelConfig
  onModelConfigChange: (config: ModelConfig) => void
}

export function ChatHeader({ 
  onClear, 
  canClear, 
  modelConfig, 
  onModelConfigChange 
}: ChatHeaderProps) {
  return (
    <header className="flex items-center justify-between border-b px-6 py-4">
      <div className="flex items-center gap-3">
        <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary text-primary-foreground">
          <Bot className="h-5 w-5" />
        </div>
        <div>
          <h1 className="text-lg font-semibold">SharpAgent</h1>
          <p className="text-sm text-muted-foreground">AI Assistant</p>
        </div>
      </div>
      <div className="flex items-center gap-2">
        <ModelSelector 
          config={modelConfig} 
          onConfigChange={onModelConfigChange} 
        />
        <Button
          variant="ghost"
          size="icon"
          onClick={onClear}
          disabled={!canClear}
          title="Clear chat"
        >
          <Trash2 className="h-5 w-5" />
        </Button>
      </div>
    </header>
  )
}
