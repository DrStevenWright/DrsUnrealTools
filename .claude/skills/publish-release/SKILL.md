---
name: publish-release
description: Publishes a new release of UE Test Tool from this repo - merges the open PR for the work (the user publishes pending work with /git:publish first), cleans up its branch, then tags, builds, releases and installs the exe. Use when the user says "Publish <version>", "Release <version>" or asks to make a new release.
---

# Publish a release

The user says something like **"Publish 0.3.0"**. That request is their go-ahead, for this run only, to merge the
work's PR, delete its local branch, push a tag and install the exe. It covers nothing beyond these steps.

## 0. The version

- Take the version from the request (`0.3.0`, or `v0.3.0`). If none was given, look up the latest tag
  (`git describe --tags --abbrev=0 --match "v[0-9]*"`), suggest the next minor and patch versions, and ask.
- It must be newer than the latest tag; `scripts/release.ps1` checks this too.

## 1. Get the work onto main

Check the state: `git status --porcelain`, the current branch, and open PRs
(`gh pr list --state open --json number,headRefName,url`).

- **Uncommitted changes, or a branch with commits that aren't on main and no PR yet:** stop and ask the user to
  run `/git:publish` (it's reserved for the user; don't invoke it or recreate its steps), then to say
  "Publish <version>" again.
- **A PR is open for the work** (e.g. the one `/git:publish` just opened):
  1. Wait for its checks: `gh pr checks <number> --watch --fail-fast`. If they fail, stop and report; don't merge.
  2. Merge it with a merge commit, as this repo does: `gh pr merge <number> --merge`.
  3. Switch to main and update it: `git switch main`, then `git pull --ff-only`.
  4. Delete the merged branch, locally only. First confirm the PR merged with the branch's current tip:
     `gh pr view <number> --json state,headRefOid` must show `MERGED` and a `headRefOid` equal to
     `git rev-parse <branch>`. If it does, `git branch -D <branch>` (`-D` because git may not see the merge
     commit as containing it), then `git remote prune origin`. If not, leave the branch and say why.
- **Leftover local branches** whose PRs merged earlier: mention them (`git branch --list`), but don't delete them;
  the user's `/git:merged` handles those.
- **Other open PRs** that aren't part of this work: mention them, but don't merge them.
- **Already on a clean main** that matches origin: go straight to step 2.

## 2. Release

Run the release script from the repo root. It takes a few minutes (it waits for two CI builds), so run it in the
background and wait for it to finish:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 <version>
```

The script checks the repo, waits for main's CI build, tags and pushes `v<version>`, waits for the release build,
checks the release, then installs `UETestTool.exe` into `%LOCALAPPDATA%\Programs\UE Test Tool`. If the app is
running it prints "Close UE Test Tool to finish installing": tell the user to close it.

If it fails, report its error. Don't delete or move a pushed tag yourself; the script's message says how, and that
is the user's call.

## 3. Report

One short paragraph: the version released, the release URL, the PR that was merged (if any), and that the installed
exe was updated (with the version it reports).
