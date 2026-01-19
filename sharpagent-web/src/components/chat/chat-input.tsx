"use client";

import { useState } from "react";
import { ThinkingLevel } from "@/types";
import {
  PromptInput,
  PromptInputTextarea,
  PromptInputActions,
  PromptInputAction,
} from "@/components/ui/prompt-input";
import { Button } from "@/components/ui/button";
import { ArrowUp, Square, Brain } from "lucide-react";

interface ChatInputProps {
  onSend: (content: string) => void;
  onStop: () => void;
  isLoading: boolean;
  thinkingLevel: ThinkingLevel;
  onThinkingLevelChange: (level: ThinkingLevel) => void;
}

const thinkingLevels: { value: ThinkingLevel; label: string }[] = [
  { value: "off", label: "Off" },
  { value: "low", label: "Low" },
  { value: "medium", label: "Medium" },
  { value: "high", label: "High" },
];

export function ChatInput({
  onSend,
  onStop,
  isLoading,
  thinkingLevel,
  onThinkingLevelChange,
}: ChatInputProps) {
  const [value, setValue] = useState("");

  const handleSubmit = () => {
    if (value.trim() && !isLoading) {
      onSend(value.trim());
      setValue("");
    }
  };

  const cycleThinkingLevel = () => {
    const currentIndex = thinkingLevels.findIndex(
      (l) => l.value === thinkingLevel,
    );
    const nextIndex = (currentIndex + 1) % thinkingLevels.length;
    onThinkingLevelChange(thinkingLevels[nextIndex].value);
  };

  return (
    <div className="border-t p-4">
      <PromptInput
        value={value}
        onValueChange={setValue}
        isLoading={isLoading}
        onSubmit={handleSubmit}
        className="mx-auto max-w-3xl"
      >
        <PromptInputTextarea placeholder="Type a message..." />
        <PromptInputActions className="justify-between pt-2">
          <div>
            <PromptInputAction
              tooltip={`Thinking: ${thinkingLevel}`}
              side="top"
            >
              <Button
                variant="ghost"
                size="sm"
                onClick={cycleThinkingLevel}
                className={
                  thinkingLevel !== "off"
                    ? "text-primary"
                    : "text-muted-foreground"
                }
              >
                <Brain className="mr-1 h-4 w-4" />
                {thinkingLevel === "off"
                  ? "Thinking"
                  : `Thinking: ${thinkingLevel}`}
              </Button>
            </PromptInputAction>
          </div>
          {isLoading ? (
            <Button
              variant="destructive"
              size="icon"
              className="h-9 w-9 rounded-full"
              onClick={onStop}
            >
              <Square className="h-4 w-4" />
            </Button>
          ) : (
            <Button
              size="icon"
              className="h-9 w-9 rounded-full"
              onClick={handleSubmit}
              disabled={!value.trim()}
            >
              <ArrowUp className="h-4 w-4" />
            </Button>
          )}
        </PromptInputActions>
      </PromptInput>
    </div>
  );
}
