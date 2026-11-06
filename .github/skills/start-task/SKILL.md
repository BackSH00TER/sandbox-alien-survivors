---
name: start-task
description: "Use when the user invokes /start-task or requests starting an implementation task in an isolated worktree. Set up isolation, then implement and validate the task in this same chat."
---

# Start task

The text supplied after `/start-task` is the task to implement. If no task was
supplied, ask for it before changing the workspace.

The user has updated main before starting this session. Set up an isolated
workspace, then complete the task in this same chat. These instructions describe
the workflow to execute, not a request for a plan.

## Workspace setup

1. Inspect the current working directory, branch, Git status (including untracked
   files), and existing worktrees. Do not modify other worktrees or their
   uncommitted changes. Never clean, stash, reset, or switch an existing checkout
   to make these checks pass.
2. Resolve local `origin/main` to a commit and record it as the fixed base for this
   task. Do not fetch or pull. If the reference is unavailable, stop and explain
   the blocker.
3. If this chat is already in a suitable fresh isolated worktree, reuse it:
   confirm that it is clean, on a dedicated task branch (not main or detached
   HEAD), and that HEAD exactly matches the fixed base. Do not treat any arbitrary
   existing branch or worktree as fresh without evidence that it belongs to this
   chat and task.
4. Otherwise, confirm that the starting checkout is clean, on main, and that
   HEAD exactly matches the fixed base. If not, stop before making changes and
   report the mismatch. Do not repair the checkout automatically.
5. Use the host's session-isolation capability to move only this chat into a NEW
   sibling worktree with a unique task branch based on that starting commit.
   Discover the tool if necessary and follow its mandatory confirmation flow.
   Do not create another chat or session, or substitute a shell-created worktree
   that leaves this chat attached to the original checkout. If session isolation
   is unavailable, stop and explain the blocker.
6. Honor the isolation tool's handoff: if it requires ending the turn after a
   successful call, make it the final tool call and end that turn. The host then
   moves this chat and resumes the task automatically. Do not poll, repeat the
   isolation request, or interpret this handoff as completion of the task.
7. After resumption, or when reusing an already suitable worktree, confirm the
   effective working directory, task branch, clean status, and HEAD matching the
   recorded base. Verify that local `origin/main` still resolves to that base.
   If any check fails, stop and report it rather than changing refs or checkouts.
   Report this setup briefly, then proceed directly into implementation.

## Implementation

- Follow repository instructions and keep changes scoped to the supplied task.
- Preserve existing behavior outside the requested change.
- Do not fetch, pull, merge, or rebase newer main changes during this task.
  Use the verified local `origin/main` as the Git review/comparison base, keeping
  the recorded commit as the fixed base. If the reference moves externally,
  report the mismatch and stop rather than silently adopting a different base.
- Complete the implementation and run relevant formatting, build, and test
  checks. Report failures and unavailable checks explicitly.
- Report the outcome, validation results, and anything not verified.
- Do not commit, push, create a PR, merge, or remove worktrees.

## Changes-view expectations

The session starts on the intended base before isolation, so no main update
should be introduced during the task. Do not claim that moving the session
resets its history or guarantees the Changes-view baseline. Retained session
history alone is not a reason to stop implementation.
