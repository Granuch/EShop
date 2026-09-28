# CI Workflow

What runs on GitHub for this repository, what each check proves, and how to reproduce it locally. There is no CD:
nothing is deployed from CI, and no image is pushed to a registry.

---

## Workflows at a Glance

| Workflow | File | Runs on | Time |
|---|---|---|---|
| CI | `.github/workflows/ci.yml` | every push to `master`, `feature/**` and `bug/**`, and pull requests into `master` | ~5 min |
| Docker Smoke | `.github/workflows/docker-smoke.yml` | push to `master`, nightly at 03:00 UTC, manual dispatch | ~5 min |
| Dependabot | `.github/dependabot.yml` | weekly | n/a |
| SonarCloud Code Analysis | the SonarCloud GitHub App (configured on sonarcloud.io, not in this repo) | pull requests | informational only, not a merge criterion |

Both workflows run on `ubuntu-24.04`, pinned so a runner-image change is a deliberate edit. They use a read-only
`GITHUB_TOKEN` (`permissions: contents: read`) and cancel an older run of the same branch.

Every action is pinned to a full commit SHA with its release in a comment (`actions/checkout@<sha> # v7.0.1`), because
a tag can be moved and a SHA cannot. Dependabot bumps both. The lint and scan tools run as pinned `docker run` images
(`rhysd/actionlint`, `hadolint/hadolint`, `aquasec/trivy`), which Dependabot does not see, so bump those by hand.

### Branch protection on `master`

`master` requires the ten CI checks to pass before a pull request can merge:
- `build-test`, `lint` and `compose-validate`;
- the seven `docker-build (<service>)` matrix jobs.

Force pushes and branch deletion are blocked, and no review is required. The owner, as an admin, can bypass the rule.
Adding, removing or renaming a CI job or a `docker-build` matrix entry means updating the required checks too. A
required check that no longer runs stays "Expected" and blocks every merge.

```bash
gh api repos/Granuch/EShop/branches/master/protection --jq '[.required_status_checks.checks[].context]'
```

---

## CI (`ci.yml`)

Four jobs run in parallel. Each one fails for its own reason.

### build-test

Restores, builds and tests `EShop.slnx` in Release, with the NuGet package cache keyed on every `.csproj`.

- The suite includes Testcontainers fixtures (PostgreSQL, Redis). The runner provides Docker, so no setup is needed.
- The `StripeSandboxTests` call the real Stripe sandbox with the `STRIPE_SANDBOX_SECRET_KEY` repository secret. For
  pushes and same-repository pull requests, `STRIPE_SANDBOX_REQUIRED=true` makes a missing key fail the run instead
  of skipping the tests. GitHub gives Dependabot and fork pull requests no secrets, so there the tests skip.
- The test results (`.trx`) are uploaded as the `ci-test-results` artifact.

### lint

- **actionlint** checks every workflow, including the shell in each `run:` block (shellcheck).
- **hadolint** checks the seven Dockerfiles with `.hadolint.yaml`. A finding at any level, info included, fails.
- **`.github/scripts/check-env-example.sh`** fails if `.env.example` holds anything that looks like a real secret.
  Every key named `*PASSWORD`, `*SECRET`, `*API_KEY` or `*_KEY` must be empty or a `CHANGE_ME`/`REPLACE_ME`
  placeholder, apart from two documented local defaults. Stripe-shaped credentials are refused under any key.

Each step runs even when an earlier one failed, so one run reports every problem.

### compose-validate

`.github/scripts/ci-env.sh` builds an env file from `.env.example`, replacing each `CHANGE_ME` value with a random one.
`.github/scripts/compose-validate.sh` then renders these combinations and fails on an error **or a warning**:

- infrastructure only;
- `sandbox`;
- `sandbox` + `monitoring`;
- `sandbox` + `stripe`;
- `development`;
- `production` + `monitoring` with the production override;
- `sandbox` + `monitoring` with the public override.

An unset variable renders as an empty string, so a warning is treated as a failure. This means **a new `${VAR:?…}` in a
compose file needs a matching line in `.env.example`**; no workflow edit is needed.

### docker-build

Builds each of the seven service images in its own matrix job. Each job uses a GitHub Actions build cache per image,
and nothing is pushed. It then runs Trivy on the image for HIGH and CRITICAL vulnerabilities, **report-only**:
- The base image (Ubuntu 24.04) had no findings on the first run.
- The one HIGH it reports is `Microsoft.OpenApi` 2.0.0. The build reports the same package as `NU1903`, and it is left
  for a dependency update.

---

## Docker Smoke (`docker-smoke.yml`)

Brings the whole stack up the way a developer would, from a cold start, and uses it:

1. `ci-env.sh` builds the env file. Notification is forced onto Mailpit through shell variables, and every trace is
   sampled.
2. `docker compose build`, then `up -d --wait` for the `sandbox` and `monitoring` profiles.
3. `.github/scripts/smoke_e2e.py` drives the whole flow through the gateway:
   - an anonymous request to a protected route gets 401;
   - register, find the confirmation email in Mailpit, check that login is refused before confirming, then confirm and
     log in;
   - add to the basket and check out; the order appears;
   - the admin settles the payment offline; the order becomes Paid, then Shipped and Delivered;
   - the customer's emails arrive;
   - every one of the seven services exported spans to Jaeger under its own name.
4. `.github/scripts/collect-logs.sh` saves every container's log, state, restart count and a `docker stats` snapshot.
5. `.github/scripts/scan_logs.py` fails the run on any `ERR`/`FTL` log entry that
   `.github/scripts/smoke-log-allowlist.txt` does not explain, and on any restarted, stopped or unhealthy container.
6. The logs are uploaded as the `docker-smoke-logs` artifact, and `down -v` runs, both always.

**Adding to the allowlist:** add one narrow regular expression, with a comment above it saying why the entry is
expected. Never loosen an existing pattern: an unrelated error from the same logger must still fail the run.

Run it on a branch with `gh workflow run "Docker Smoke" --ref <branch>`.

---

## Dependabot (`dependabot.yml`)

Weekly, for four ecosystems:
- **NuGet**: grouped by version family (`dotnet-platform`, `messaging`, `cqrs`, `opentelemetry`, `testing`, …), so
  packages that must move together, such as ASP.NET Core, EF Core, `Microsoft.Extensions.*` and Npgsql EF, arrive in
  one pull request.
- **GitHub Actions**: one `actions` group; updates SHA pins together with their comments.
- **Docker**: the seven Dockerfiles' base images.
- **Docker Compose**: the image tags in `docker-compose.yml`; major versions are ignored.

Dependabot reads its configuration from `master` only, so a change to `dependabot.yml` takes effect once merged.
Validate it first:

```bash
python -m check_jsonschema --schemafile https://www.schemastore.org/dependabot-2.0.json .github/dependabot.yml
```

---

## Reproducing CI Locally

```bash
# lint: workflows and Dockerfiles, the same images CI uses
docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:1.7.12
for f in src/ApiGateways/*/Dockerfile src/Services/*/*.API/Dockerfile; do
  docker run --rm -i -v "$PWD/.hadolint.yaml:/.config/hadolint.yaml:ro" hadolint/hadolint:v2.15.1 \
    hadolint --config /.config/hadolint.yaml - < "$f"
done
bash .github/scripts/check-env-example.sh

# compose-validate
bash .github/scripts/ci-env.sh /tmp/ci.env && bash .github/scripts/compose-validate.sh /tmp/ci.env

# build-test (unset any ambient JWT variables, or you are testing your shell rather than the repo)
env -u JwtSettings__SecretKey -u JWT_SIGNING_KEY dotnet test EShop.slnx -c Release

# Docker Smoke's checks, against your own running stack
python .github/scripts/smoke_e2e.py
bash .github/scripts/collect-logs.sh smoke-logs
python .github/scripts/scan_logs.py smoke-logs .github/scripts/smoke-log-allowlist.txt
```

On Windows with Git Bash, prefix the `docker run` lines with `MSYS_NO_PATHCONV=1` and use `$(pwd -W)` instead of
`$PWD`.

Do not run the full smoke workflow locally with a generated env file. Compose uses fixed volume and container names
(`eshop-*`), so it would reuse your local data volumes, and its `down -v` would delete them.

---

## Changing a Workflow

- Run actionlint before pushing.
- Prove that a new check fails for the right reason. Push the deliberate breaks to a throwaway branch
  (`feature/<topic>-falsify`), since CI runs on every `feature/**` push. Put each break in a different job, read which
  jobs went red, then delete the branch. Nothing needs reverting in the real history.
- `gh run list --commit <sha>` needs the full 40-character SHA. A run can take 20 s or more to appear after a push.
- Pull requests run the workflow file from their merge commit, so a workflow change on a branch takes effect for that
  pull request before it reaches `master`.

---

## Related Documents

- [Docker Setup](../02-getting-started/docker-setup.md)
- [Code Review Process](code-review-process.md)

---

**Last Updated**: 2026-09-28
