"use client"

import { ChatMessage } from "@/types"
import {
  ChatContainerRoot,
  ChatContainerContent,
  ChatContainerScrollAnchor,
} from "@/components/ui/chat-container"
import { ScrollButton } from "@/components/ui/scroll-button"
import { Message, MessageAvatar, MessageContent } from "@/components/ui/message"
import { Reasoning, ReasoningTrigger, ReasoningContent } from "@/components/ui/reasoning"
import { Tool, ToolPart } from "@/components/ui/tool"
import { Loader } from "@/components/ui/loader"
import { Bot, User, Sparkles } from "lucide-react"

interface ChatMessagesProps {
  messages: ChatMessage[]
  isLoading: boolean
}

export function ChatMessages({ messages, isLoading }: ChatMessagesProps) {
  if (messages.length === 0) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8">
        <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-primary/10">
          <Sparkles className="h-8 w-8 text-primary" />
        </div>
        <div className="text-center">
          <h2 className="text-xl font-semibold">Welcome to SharpAgent</h2>
          <p className="mt-2 text-muted-foreground">
            Type a message below to start a conversation
          </p>
        </div>
      </div>
    )
  }

  return (
    <ChatContainerRoot className="relative flex-1">
      <ChatContainerContent className="gap-6 p-6">
        {messages.map((message) => (
          <MessageBubble key={message.id} message={message} />
        ))}
        {isLoading && messages.length > 0 && messages[messages.length - 1].role === "user" && (
          <Message>
            <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground">
              <Bot className="h-4 w-4" />
            </div>
            <div className="flex items-center gap-2 rounded-lg bg-secondary p-3">
              <Loader variant="typing" size="sm" />
            </div>
          </Message>
        )}
        <ChatContainerScrollAnchor />
      </ChatContainerContent>
      <ScrollButton className="absolute bottom-4 right-4" />
    </ChatContainerRoot>
  )
}

function MessageBubble({ message }: { message: ChatMessage }) {
  const isUser = message.role === "user"

  return (
    <Message className={isUser ? "flex-row-reverse" : ""}>
      <div
        className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full ${
          isUser
            ? "bg-secondary text-secondary-foreground"
            : "bg-primary text-primary-foreground"
        }`}
      >
        {isUser ? <User className="h-4 w-4" /> : <Bot className="h-4 w-4" />}
      </div>
      <div className={`flex max-w-[80%] flex-col gap-2 ${isUser ? "items-end" : "items-start"}`}>
        {message.reasoning && (
          <Reasoning>
            <ReasoningTrigger className="text-sm text-muted-foreground hover:text-foreground">
              View reasoning
            </ReasoningTrigger>
            <ReasoningContent className="mt-2" markdown>
              {message.reasoning}
            </ReasoningContent>
          </Reasoning>
        )}
        
        {message.toolCalls?.map((tool) => {
          const toolPart: ToolPart = {
            type: tool.name,
            state: tool.state,
            input: tool.input,
            output: tool.output,
            toolCallId: tool.id,
            errorText: tool.errorText,
          }
          return <Tool key={tool.id} toolPart={toolPart} />
        })}

        {(message.content || message.isStreaming) && (
          <MessageContent
            markdown={!isUser}
            className={
              isUser
                ? "bg-primary text-primary-foreground"
                : "bg-secondary"
            }
          >
            {message.content || (message.isStreaming ? "..." : "")}
          </MessageContent>
        )}
      </div>
    </Message>
  )
}
