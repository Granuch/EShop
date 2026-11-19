# Repository Settings

How the GitHub repository is configured, why, and the `gh` commands to check or reapply each setting. The repository
is public, so anyone can fork it and open a pull request; most of these settings exist to keep such a pull request
from running code with our secrets or reaching anything we deploy.

All commands need the GitHub CLI signed in as a repository admin. Read commands are safe to run at any time.

```bash
R=repos/Granuch/EShop
```

---

## Actions

| Setting | Value | Why |
|---|---|---|
| Fork pull request workflows | approval required for **all external contributors** | A pull request from a fork runs nothing until a maintainer clicks "Approve and run". The default only asked first-time contributors. Read the diff before approving: build and test steps execute the pull request's code. |
| Allowed actions | GitHub-owned actions plus `docker/*`, `fluxcd/*` and `dependabot/*` | A workflow cannot pull in an arbitrary third-party action. A new action from another owner must be added to this list first. |
| Require actions pinned to a full SHA | on | A tag can be moved to different code; a commit SHA cannot. Every workflow already pins its actions, and Dependabot bumps SHA and comment together. |
| Default `GITHUB_TOKEN` permissions | read-only; cannot approve pull requests | Each job asks for any extra permission itself (`permissions:` in the workflow), so only the jobs that need it get it. |
| Artifact and log retention | 30 days | Test results and smoke logs are useful for a few weeks, not three months. |

```bash
gh api $R/actions/permissions                              # allowed_actions, sha_pinning_required
gh api $R/actions/permissions/selected-actions             # the allowlist
gh api $R/actions/permissions/fork-pr-contributor-approval
gh api $R/actions/permissions/workflow                     # default token permissions
gh api $R/actions/permissions/artifact-and-log-retention

# Reapply
gh api -X PUT $R/actions/permissions/fork-pr-contributor-approval -f approval_policy=all_external_contributors
gh api -X PUT $R/actions/permissions -F enabled=true -f allowed_actions=selected -F sha_pinning_required=true
echo '{"github_owned_allowed":true,"verified_allowed":false,"patterns_allowed":["docker/*","fluxcd/*","dependabot/*"]}' \
  | gh api -X PUT $R/actions/permissions/selected-actions --input -
gh api -X PUT $R/actions/permissions/artifact-and-log-retention -F days=30
```

Fork and Dependabot pull requests never receive repository secrets, whatever these settings say. That is why
`ci.yml` skips the Stripe sandbox tests there instead of failing.

## Security features

| Setting | Value | Why |
|---|---|---|
| Secret scanning | on | GitHub scans the whole history for known credential formats (Stripe keys, cloud tokens, …) and opens an alert. |
| Push protection | on | A push that contains such a credential is refused before it reaches GitHub. |
| Dependabot alerts | on | Known vulnerabilities in NuGet, npm, Docker and Actions dependencies are listed under Security. |
| Dependabot security updates | on | Dependabot opens a pull request for a vulnerable dependency without waiting for the weekly version run. |
| Private vulnerability reporting | on | Lets anyone report a problem privately; see [SECURITY.md](../../SECURITY.md). |
| Code scanning (CodeQL default setup) | on | CodeQL analyses C#, TypeScript/JavaScript, Python and the workflows on every push and pull request into `master`. Its findings appear under Security; it is not a required check. |

Scanning for generic (non-provider) secret patterns and secret validity checks need GitHub's paid Secret Protection,
so they stay off.

```bash
gh api $R --jq .security_and_analysis
gh api $R/automated-security-fixes        # Dependabot security updates
gh api $R/private-vulnerability-reporting
gh api $R/code-scanning/default-setup
```

## Pull requests and merging

| Setting | Value | Why |
|---|---|---|
| Merge methods | squash only | One commit per pull request on `master`, titled with the pull request's title. The ruleset enforces the same. |
| "Update branch" button | on | Brings a pull request up to date with `master` without a local merge. |
| Auto-merge | on | A pull request can be set to merge itself once every required check passes. `dependabot-auto-merge.yml` does this for low-risk Dependabot updates; see [ci-cd-workflow.md](ci-cd-workflow.md#automatic-merging). |
| Delete head branch on merge | on | A squash merge puts all of a pull request's changes on `master`, so its branch holds nothing more. A deleted branch can be brought back with "Restore branch" on its pull request. The branch of a pull request closed without merging stays: delete it by hand (`git push origin --delete <branch>`). If a long-lived branch is ever used as the head of a pull request into `master`, turn this off first, or the merge deletes that branch. |
| Wiki, Projects | off | Unused; documentation lives in `docs/`. |

```bash
gh api $R --jq '{allow_squash_merge, allow_merge_commit, allow_rebase_merge, allow_update_branch, allow_auto_merge,
  delete_branch_on_merge, has_wiki, has_projects}'
```

## Rulesets

There is no classic branch protection; two rulesets replace it.

**`master`** (branch ruleset):
- pull request required, squash merge only;
- the CI checks listed in [ci-cd-workflow.md](ci-cd-workflow.md#branch-rules-on-master) must pass;
- every review conversation resolved;
- no required review: a collaborator merges on green CI;
- linear history, no force push, no deletion.

**`release tags`** (tag ruleset): tags matching `v*` can only be created, moved or deleted by an admin.

Repository admins can bypass both, for an emergency. Every other contributor goes through the rules.

```bash
gh api $R/rulesets --jq '.[] | {id, name, target, enforcement}'
gh api $R/rules/branches/master --jq '[.[].type]'
gh api $R/rulesets/<id>                    # full definition, for a backup before editing
```

A ruleset is edited with `gh api -X PUT $R/rulesets/<id> --input ruleset.json`, sending the whole definition. Read the
current one first and change only what you mean to.

## Environments

**`production`**: only runs from `master` may use it, so only a merged commit can be deployed or rolled back to. It
has no required reviewer: every green merge is deployed without a click (decided 2026-11-18).

```bash
gh api $R/environments/production --jq '{protection_rules, deployment_branch_policy}'
gh api $R/environments/production/deployment-branch-policies

# Gate deployments by hand again (the deploy job then waits at "Review deployments" until a reviewer approves):
echo '{"reviewers":[{"type":"User","id":'"$(gh api users/Granuch -q .id)"'}],
  "deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' \
  | gh api -X PUT $R/environments/production --input -
```

## Collaborators and outside contributors

Who can do what:
- **Collaborators with Write access** (the owner and Yarickprogramist) push branches, run CI with the repository's
  secrets, and merge a pull request once its checks pass; no review is required. A workflow on a branch they push can
  read the secrets, so every collaborator should have two-factor authentication on their GitHub account.
- **Anyone else** works from a fork. Their pull request runs no workflow until a collaborator clicks "Approve and run"
  (see [Actions](#actions)), gets no secrets even then, and can only be merged by a collaborator.

```bash
gh api $R/collaborators --jq '.[] | .login + " " + .role_name'
```
