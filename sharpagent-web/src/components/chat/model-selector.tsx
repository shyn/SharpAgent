"use client"

import { useState } from "react"
import { Provider, ModelConfig, PROVIDER_MODELS } from "@/types"
import { Button } from "@/components/ui/button"
import { Settings, ChevronDown, Key, Check } from "lucide-react"

interface ModelSelectorProps {
  config: ModelConfig
  onConfigChange: (config: ModelConfig) => void
}

export function ModelSelector({ config, onConfigChange }: ModelSelectorProps) {
  const [isOpen, setIsOpen] = useState(false)
  const [showApiKeyInput, setShowApiKeyInput] = useState(false)
  const [tempApiKey, setTempApiKey] = useState(config.apiKey)

  const handleProviderChange = (provider: Provider) => {
    const models = PROVIDER_MODELS[provider]
    onConfigChange({
      ...config,
      provider,
      model: models[0],
    })
  }

  const handleModelChange = (model: string) => {
    onConfigChange({ ...config, model })
  }

  const handleApiKeySave = () => {
    onConfigChange({ ...config, apiKey: tempApiKey })
    setShowApiKeyInput(false)
  }

  const hasApiKey = config.apiKey.length > 0

  return (
    <div className="relative">
      <Button
        variant="outline"
        size="sm"
        onClick={() => setIsOpen(!isOpen)}
        className="gap-2"
      >
        <Settings className="h-4 w-4" />
        <span className="hidden sm:inline">{config.provider === "openai" ? "OpenAI" : "Anthropic"}</span>
        <span className="text-muted-foreground text-xs hidden md:inline">/ {config.model}</span>
        <ChevronDown className={`h-3 w-3 transition-transform ${isOpen ? "rotate-180" : ""}`} />
      </Button>

      {isOpen && (
        <div className="absolute right-0 top-full z-50 mt-2 w-80 rounded-lg border bg-popover p-4 shadow-lg">
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium">Provider</label>
              <div className="mt-2 flex gap-2">
                {(["openai", "anthropic"] as Provider[]).map((p) => (
                  <Button
                    key={p}
                    variant={config.provider === p ? "default" : "outline"}
                    size="sm"
                    onClick={() => handleProviderChange(p)}
                    className="flex-1"
                  >
                    {p === "openai" ? "OpenAI" : "Anthropic"}
                  </Button>
                ))}
              </div>
            </div>

            <div>
              <label className="text-sm font-medium">Model</label>
              <div className="mt-2 grid grid-cols-2 gap-2">
                {PROVIDER_MODELS[config.provider].map((model) => (
                  <Button
                    key={model}
                    variant={config.model === model ? "default" : "outline"}
                    size="sm"
                    onClick={() => handleModelChange(model)}
                    className="text-xs justify-start truncate"
                    title={model}
                  >
                    {model.replace("claude-", "").replace("gpt-", "")}
                  </Button>
                ))}
              </div>
            </div>

            <div>
              <div className="flex items-center justify-between">
                <label className="text-sm font-medium">API Key</label>
                {hasApiKey && !showApiKeyInput && (
                  <span className="flex items-center gap-1 text-xs text-green-600">
                    <Check className="h-3 w-3" /> Configured
                  </span>
                )}
              </div>
              {showApiKeyInput ? (
                <div className="mt-2 space-y-2">
                  <input
                    type="password"
                    value={tempApiKey}
                    onChange={(e) => setTempApiKey(e.target.value)}
                    placeholder={config.provider === "openai" ? "sk-..." : "sk-ant-..."}
                    className="w-full rounded-md border bg-background px-3 py-2 text-sm"
                  />
                  <div className="flex gap-2">
                    <Button size="sm" onClick={handleApiKeySave} className="flex-1">
                      Save
                    </Button>
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => {
                        setShowApiKeyInput(false)
                        setTempApiKey(config.apiKey)
                      }}
                    >
                      Cancel
                    </Button>
                  </div>
                </div>
              ) : (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setShowApiKeyInput(true)}
                  className="mt-2 w-full gap-2"
                >
                  <Key className="h-4 w-4" />
                  {hasApiKey ? "Change API Key" : "Set API Key"}
                </Button>
              )}
              <p className="mt-2 text-xs text-muted-foreground">
                Your API key is stored locally in your browser.
              </p>
            </div>
          </div>
        </div>
      )}

      {isOpen && (
        <div
          className="fixed inset-0 z-40"
          onClick={() => setIsOpen(false)}
        />
      )}
    </div>
  )
}
