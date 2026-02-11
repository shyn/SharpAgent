#!/usr/bin/env node

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";

const DEFAULT_MODELS_DEV_URL = "https://models.dev/api.json";
const DEFAULT_OUTPUT_PATH = "Sharp.Core/Configuration/BuiltInPiProviders.generated.cs";

/**
 * pi-aligned built-in subset for Sharp.
 *
 * Scope note:
 * - This intentionally tracks a curated subset of pi built-ins.
 * - It is not a full mirror of models.dev.
 * - We only include providers that map cleanly to Sharp's currently supported API kinds.
 */
const PI_BUILTIN_SPECS = [
  {
    providerId: "openrouter",
    sourceKey: "openrouter",
    preferredModelIds: ["openai/gpt-5.1-codex"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://openrouter.ai/api/v1",
  },
  {
    providerId: "xai",
    sourceKey: "xai",
    preferredModelIds: ["grok-4-fast-non-reasoning"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://api.x.ai/v1",
  },
  {
    providerId: "groq",
    sourceKey: "groq",
    preferredModelIds: ["openai/gpt-oss-120b"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://api.groq.com/openai/v1",
  },
  {
    providerId: "cerebras",
    sourceKey: "cerebras",
    preferredModelIds: ["zai-glm-4.6", "zai-glm-4.7"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://api.cerebras.ai/v1",
  },
  {
    providerId: "zai",
    sourceKey: "zai",
    preferredModelIds: ["glm-4.6"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://api.z.ai/api/coding/paas/v4",
  },
  {
    providerId: "mistral",
    sourceKey: "mistral",
    preferredModelIds: ["devstral-medium-latest"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://api.mistral.ai/v1",
  },
  {
    providerId: "minimax",
    sourceKey: "minimax",
    preferredModelIds: ["MiniMax-M2.1"],
    apiFormat: "ModelApiFormat.AnthropicMessages",
    baseUrl: "https://api.minimax.io/anthropic/v1",
  },
  {
    providerId: "minimax-cn",
    sourceKey: "minimax-cn",
    preferredModelIds: ["MiniMax-M2.1"],
    apiFormat: "ModelApiFormat.AnthropicMessages",
    baseUrl: "https://api.minimaxi.com/anthropic/v1",
  },
  {
    providerId: "huggingface",
    sourceKey: "huggingface",
    preferredModelIds: ["moonshotai/Kimi-K2.5"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://router.huggingface.co/v1",
  },
  {
    providerId: "opencode",
    sourceKey: "opencode",
    preferredModelIds: ["claude-opus-4-6"],
    apiFormat: "ModelApiFormat.AnthropicMessages",
    baseUrl: "https://opencode.ai/zen/v1",
  },
  {
    providerId: "github-copilot",
    sourceKey: "github-copilot",
    preferredModelIds: ["gpt-4o"],
    apiFormat: "ModelApiFormat.OpenAiCompletions",
    baseUrl: "https://api.individual.githubcopilot.com",
  },
  {
    providerId: "kimi-coding",
    sourceKey: "kimi-for-coding",
    preferredModelIds: ["kimi-k2-thinking"],
    apiFormat: "ModelApiFormat.AnthropicMessages",
    baseUrl: "https://api.kimi.com/coding/v1",
  },
];

function parseArgs(argv) {
  const args = {
    inputFile: null,
    modelsDevUrl: DEFAULT_MODELS_DEV_URL,
    sourceLabel: null,
    outputPath: DEFAULT_OUTPUT_PATH,
  };

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if ((arg === "--input-file" || arg === "-i") && i + 1 < argv.length) {
      args.inputFile = argv[++i];
      continue;
    }
    if ((arg === "--url" || arg === "-u") && i + 1 < argv.length) {
      args.modelsDevUrl = argv[++i];
      continue;
    }
    if (arg === "--source-label" && i + 1 < argv.length) {
      args.sourceLabel = argv[++i];
      continue;
    }
    if ((arg === "--output" || arg === "-o") && i + 1 < argv.length) {
      args.outputPath = argv[++i];
      continue;
    }
    throw new Error(`Unknown argument: ${arg}`);
  }

  return args;
}

async function loadModelsData(args) {
  if (args.inputFile) {
    const path = resolve(process.cwd(), args.inputFile);
    return JSON.parse(readFileSync(path, "utf8"));
  }

  const response = await fetch(args.modelsDevUrl);
  if (!response.ok) {
    throw new Error(`Failed to fetch ${args.modelsDevUrl}: HTTP ${response.status}`);
  }
  return await response.json();
}

function normalizeBaseUrl(baseUrl, apiFormat) {
  const trimmed = baseUrl.trim().replace(/\/+$/, "");
  if (apiFormat === "ModelApiFormat.AnthropicMessages") {
    if (trimmed.endsWith("/v1/messages")) {
      return `${trimmed.slice(0, -"/messages".length)}/`;
    }
    if (trimmed.endsWith("/v1")) {
      return `${trimmed}/`;
    }
    return `${trimmed}/v1/`;
  }

  return `${trimmed}/`;
}

function getToolCapableModel(modelNode) {
  return modelNode && typeof modelNode === "object" && modelNode.tool_call === true;
}

function selectModel(providerData, spec) {
  const modelsNode = providerData?.models;
  if (!modelsNode || typeof modelsNode !== "object") {
    throw new Error(`Provider '${spec.sourceKey}' has no models`);
  }

  for (const preferredId of spec.preferredModelIds) {
    const model = modelsNode[preferredId];
    if (getToolCapableModel(model)) {
      return { id: preferredId, model };
    }
  }

  const fallback = Object.entries(modelsNode)
    .filter(([, model]) => getToolCapableModel(model))
    .map(([id, model]) => ({ id, model }))
    .sort((a, b) => a.id.localeCompare(b.id))[0];

  if (!fallback) {
    throw new Error(`Provider '${spec.sourceKey}' has no tool-capable models`);
  }

  return fallback;
}

function toModelConfigLine(model) {
  const segments = [
    `Id = "${model.id}"`,
    `ContextWindow = ${model.contextWindow}`,
    `MaxOutputTokens = ${model.maxOutputTokens}`,
  ];

  if (model.capabilities) {
    segments.push(`Capabilities = new ModelCapabilitiesConfig
                        {
                            SupportsReasoning = ${model.capabilities.supportsReasoning},
                            SupportsImageInput = ${model.capabilities.supportsImageInput},
                            SupportsToolCall = ${model.capabilities.supportsToolCall}
                        }`);
  }

  if (model.pricing) {
    segments.push(`Pricing = new ModelPricingConfig
                        {
                            InputPerMillionTokens = ${model.pricing.inputPerMillionTokens}m,
                            OutputPerMillionTokens = ${model.pricing.outputPerMillionTokens}m,
                            CacheReadPerMillionTokens = ${model.pricing.cacheReadPerMillionTokens}m,
                            CacheWritePerMillionTokens = ${model.pricing.cacheWritePerMillionTokens}m
                        }`);
  }

  return `                    new ModelConfig
                    {
                        ${segments.join(",\n                        ")}
                    },`;
}

function renderGeneratedClass(sourceLabel, providers) {
  const providerBlocks = providers
    .map((provider) => {
      const modelLines = provider.models.map(toModelConfigLine).join("\n");
      return `            new ProviderConfig
            {
                Id = "${provider.id}",
                Api = ${provider.apiFormat},
                BaseUrl = "${provider.baseUrl}",
                Models =
                [
${modelLines}
                ]
            },`;
    })
    .join("\n");

  return `// <auto-generated />
// Source: ${sourceLabel}
// Generator: scripts/generate-pi-builtin-providers.mjs

namespace Sharp.Core.Configuration;

internal static class BuiltInPiProviders
{
    public static IReadOnlyList<ProviderConfig> Create()
        => new List<ProviderConfig>
        {
${providerBlocks}
        };
}
`;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const data = await loadModelsData(args);

  const providers = PI_BUILTIN_SPECS.map((spec) => {
    const providerData = data?.[spec.sourceKey];
    if (!providerData) {
      throw new Error(`Missing provider key '${spec.sourceKey}' in source data`);
    }

    const { id: modelId, model } = selectModel(providerData, spec);
    const contextWindow = Number.isFinite(model?.limit?.context) ? model.limit.context : 4096;
    const maxOutputTokens = Number.isFinite(model?.limit?.output) ? model.limit.output : 4096;

    return {
      id: spec.providerId,
      apiFormat: spec.apiFormat,
      baseUrl: normalizeBaseUrl(spec.baseUrl, spec.apiFormat),
      models: [
        {
          id: modelId,
          contextWindow,
          maxOutputTokens,
        },
      ],
    };
  });

  // models.dev does not expose Google Antigravity private catalog.
  // Keep this provider explicitly aligned with pi-mono generated models.
  providers.push({
    id: "google-antigravity",
    apiFormat: "ModelApiFormat.GoogleGeminiCli",
    baseUrl: "https://daily-cloudcode-pa.sandbox.googleapis.com/",
    models: [
      {
        id: "gemini-3-pro-high",
        contextWindow: 1048576,
        maxOutputTokens: 65535,
        capabilities: {
          supportsReasoning: true,
          supportsImageInput: true,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 2,
          outputPerMillionTokens: 12,
          cacheReadPerMillionTokens: 0.2,
          cacheWritePerMillionTokens: 2.375,
        },
      },
      {
        id: "gemini-3-pro-low",
        contextWindow: 1048576,
        maxOutputTokens: 65535,
        capabilities: {
          supportsReasoning: true,
          supportsImageInput: true,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 2,
          outputPerMillionTokens: 12,
          cacheReadPerMillionTokens: 0.2,
          cacheWritePerMillionTokens: 2.375,
        },
      },
      {
        id: "gemini-3-flash",
        contextWindow: 1048576,
        maxOutputTokens: 65535,
        capabilities: {
          supportsReasoning: true,
          supportsImageInput: true,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 0.5,
          outputPerMillionTokens: 3,
          cacheReadPerMillionTokens: 0.5,
          cacheWritePerMillionTokens: 0,
        },
      },
      {
        id: "claude-sonnet-4-5",
        contextWindow: 200000,
        maxOutputTokens: 64000,
        capabilities: {
          supportsReasoning: false,
          supportsImageInput: true,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 3,
          outputPerMillionTokens: 15,
          cacheReadPerMillionTokens: 0.3,
          cacheWritePerMillionTokens: 3.75,
        },
      },
      {
        id: "claude-sonnet-4-5-thinking",
        contextWindow: 200000,
        maxOutputTokens: 64000,
        capabilities: {
          supportsReasoning: true,
          supportsImageInput: true,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 3,
          outputPerMillionTokens: 15,
          cacheReadPerMillionTokens: 0.3,
          cacheWritePerMillionTokens: 3.75,
        },
      },
      {
        id: "claude-opus-4-5-thinking",
        contextWindow: 200000,
        maxOutputTokens: 64000,
        capabilities: {
          supportsReasoning: true,
          supportsImageInput: true,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 5,
          outputPerMillionTokens: 25,
          cacheReadPerMillionTokens: 0.5,
          cacheWritePerMillionTokens: 6.25,
        },
      },
      {
        id: "claude-opus-4-6-thinking",
        contextWindow: 200000,
        maxOutputTokens: 128000,
        capabilities: {
          supportsReasoning: true,
          supportsImageInput: true,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 5,
          outputPerMillionTokens: 25,
          cacheReadPerMillionTokens: 0.5,
          cacheWritePerMillionTokens: 6.25,
        },
      },
      {
        id: "gpt-oss-120b-medium",
        contextWindow: 131072,
        maxOutputTokens: 32768,
        capabilities: {
          supportsReasoning: false,
          supportsImageInput: false,
          supportsToolCall: true,
        },
        pricing: {
          inputPerMillionTokens: 0.09,
          outputPerMillionTokens: 0.36,
          cacheReadPerMillionTokens: 0,
          cacheWritePerMillionTokens: 0,
        },
      },
    ],
  });

  const sourceLabel = args.sourceLabel
    ?? (args.inputFile ? `file:${args.inputFile}` : args.modelsDevUrl);
  const output = renderGeneratedClass(sourceLabel, providers);
  const outputPath = resolve(process.cwd(), args.outputPath);

  mkdirSync(dirname(outputPath), { recursive: true });
  writeFileSync(outputPath, output);

  console.log(`Generated ${outputPath} with ${providers.length} providers`);
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
});
