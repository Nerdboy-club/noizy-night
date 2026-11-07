# Game Studio Agent Architecture (Antigravity & Claude Code)

This workspace is configured with **Claude Code Game Studios** (49 agents, 74 skills, 14 rules, 15 hooks).

See [CLAUDE.md](file:///C:/Vs-code/gamedev/noizy-night/CLAUDE.md) for full studio instructions, engine configuration, and workflow protocols.

## Key Directories
- `.claude/skills/` (and `.agents/skills/`): 74 workflow skills
- `.claude/agents/`: 49 specialized game studio agent profiles
- `.claude/rules/` (and `.agents/rules/`): Game development rules and standards
- `design/`: Game design documents, briefs, and systems
- `production/`: Epics, stories, sprints, QA evidence
- `docs/`: Architecture (ADRs), engine reference, and standards
- `project.yaml`: Central project configuration

## Workflow Entry Points
- `/start`: Onboarding & workflow setup
- `/adopt`: Brownfield audit of existing project artifacts
- `/setup-engine`: Configure engine (Godot, Unity, Unreal, etc.)
- `/brainstorm`: Game concept ideation
- `/dev-story` / `/story-done`: Story execution pipeline
