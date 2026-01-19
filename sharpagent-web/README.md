# SharpAgent Web

Next.js frontend for SharpAgent with real-time streaming chat.

## Prerequisites

- Node.js 20+
- .NET 10 SDK
- API key for OpenAI or Anthropic (configured via environment variables or config.json)

## Running

### 1. Start the API server

```bash
cd SharpAgent.Api
dotnet run
```

The API will start on http://localhost:5000

### 2. Start the frontend (in another terminal)

```bash
cd sharpagent-web
npm install
npm run dev
```

The frontend will start on http://localhost:3000

## Configuration

Set API keys via environment variables:

```bash
export OPENAI_API_KEY=sk-...
# or
export ANTHROPIC_API_KEY=sk-ant-...
```

Or create a `config.json` in the API project directory.

## API Endpoints

- `GET /api/config` - Get current configuration
- `POST /api/chat` - Stream chat messages (SSE)
  - Body: `{ "message": "...", "thinkingLevel": "off|low|medium|high" }`
