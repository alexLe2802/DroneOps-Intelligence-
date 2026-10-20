# Pull request checks

Workflow: `.github/workflows/ci.yml`. Runs on every pull request (including forks), pushes to `main`, `be-develop` and `Qthong`, merge queue checks, and manual dispatch.

Required check name: **PR Quality Gate**. It fails if either Backend checks or Frontend checks fails, is skipped or is cancelled. No path filters: documentation-only PRs also produce the required check.

## Checks

- Backend: .NET 8 restore, Release build with warnings as errors, .NET analyzer diagnostics, then the AuthChecks executable's offline authorization/validation checks. AuthChecks is a console runner, not a `dotnet test` project; the workflow explicitly executes it so failures block the gate.
- Frontend: Node 24, `npm ci`, ESLint with zero warnings, Next.js route type generation / TypeScript, `npm test` for the Node test runner suites, and the production Next.js build.
- Actions are pinned to commit SHAs; Dependabot proposes action updates weekly. Workflow permissions are read-only and checkout does not persist credentials.
- No Supabase, SMTP, Firebase or deployment secrets are required. The live `--license-workflow` check is deliberately excluded because it writes temporary data to a configured database/Storage. Browser and real SMTP delivery tests are not included in this workflow.

## Make CI mandatory before merge

An administrator must configure this in GitHub; adding the workflow alone does not prevent merges.

1. Push this workflow and let CI run once (open/update a PR if needed).
2. Open repository Settings → Branches → branch protection for `main` (or Settings → Rules → Rulesets).
3. Require a pull request before merging.
4. Require status checks to pass, choose **PR Quality Gate**, and require the branch to be up to date before merging.
5. Require conversation resolution, disable force pushes/deletions, and do not allow bypass if all merges must pass CI. Preserve existing reviewer/approval requirements.
6. Repeat for `be-develop` if PRs also merge there. If using merge queue, this workflow handles `merge_group`.

The connected GitHub App does not have repository Administration permission (branch-protection API returns 403), so it cannot enable or verify these settings automatically.

## Local commands

```sh
dotnet restore be/DroneOps/DroneOps.sln
dotnet build be/DroneOps/DroneOps.sln -c Release --no-restore -warnaserror
dotnet format analyzers be/DroneOps/DroneOps.sln --no-restore --verify-no-changes --severity warn
dotnet run --project be/DroneOps/DroneOps.AuthChecks -c Release
cd fe
npm ci
npm run lint -- --max-warnings=0
npm run typecheck
npm test
npm run build
```

This configuration validates changes before merge. Deployment needs a hosting target and environment configuration and is not enabled here.
