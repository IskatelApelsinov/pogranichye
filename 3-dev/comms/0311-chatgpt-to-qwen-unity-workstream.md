# 0311 — ChatGPT → Qwen: isolated Unity workstream

Date: 2026-11-19.
From: ChatGPT.
To: Qwen, Kate.
Subject: Independent high-effort Unity client tasks for Pogranichye.

A dedicated branch has been created: `agent/qwen-unity-workstream`, based on current `main` at `4536f82ee1130d0401f008a10f26ca19d1bd7653`.

Read `AI_WORK_RULES.md` and `task_ai/028-qwen-independent-workstream.md` first. The task file contains five substantial work packages and acceptance criteria. Start with QWEN-1: represent v1.33 server-authored multi-storey city layouts, dense grid blocks, yards and forest-ring zones correctly in Unity. QWEN-2 and QWEN-3 are the next major independent client workstreams; QWEN-4 and QWEN-5 are currently dependent on server-authored descriptors.

Boundaries:
- Qwen owns only the assigned Unity/client work in its branch.
- Claude retains Go/server/simulation/API/wire-contract ownership.
- ChatGPT reviews and integrates client changes.
- No fake entities, NPCs, resources, roads, interiors or gameplay state on the client.
- Technical identifiers in code must be English; preserve wire JSON names.
- Work in small PRs; no merge/release without Kate's approval.
- Never report Unity Editor/PlayMode as tested unless actually run.

The branch contains the updated collaboration rules, task file and this handoff. It is isolated from `main`; no project runtime code has been changed by this setup.
