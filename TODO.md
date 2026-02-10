- [ ] Add search tool with duckduckgo (https://github.com/open-webui/open-webui/blob/a7271532/backend/open_webui/retrieval/web/duckduckgo.py)
- [x] Add core file discovery tools (`grep`, `find`, `ls`)
- [ ] Add  fetch tool which fetch a webpage returns markdown
- [ ] Add subagent support
- [x] Support AGENTS.md
- [x] Support skills

## Plugin roadmap (core)

- [ ] Implement plugin isolation loading with `AssemblyLoadContext` (collectible)
- [ ] Add automatic extension hot reload trigger (watcher + debounce)
- [ ] Add provider factory rollback on extension removal/reload
- [ ] Define plugin package spec (manifest + resources layout)

## CLI roadmap (host)

- [x] Add thin CLI host project (`Sharp.Cli`) with `run`/`repl`/`models`
- [ ] Add structured output mode (`--output json|jsonl|text`)
- [ ] Add complete session subcommands (`session tree|fork|switch|continue`)
- [ ] Add smoke tests for CLI command parsing and non-interactive flows
