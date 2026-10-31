# Security and trust

ODM manages devices: the agent runs as root / SYSTEM and executes jobs it receives from the server. Whoever controls the
server can therefore run code on all devices. This page describes what protects that, how to check the project yourself and
which gaps are known.

## Threat model in short

| Threat | Protection | Gap |
|---|---|---|
| Stranger reaches the API | JWT sign-in, role policies, rate limit on sign-in and password change, audit log | guarded by a test that lists all anonymous endpoints |
| Rogue device registers | one-time or limited registration tokens (stored hashed), optional mTLS | reusable tokens are valid until used up or expired |
| Agent impersonation | per-device secret, optional client certificate from the agent CA | see mTLS mode in [operations](operations.md) |
| **Compromised server pushes malicious code** | none beyond access control of the server | **agent updates and packages are verified against a checksum that the server itself provides, not against a signature** (see risks in arc42, section 11) |
| Malicious dependency | lock files, Dependabot, `npm audit`, NuGet vulnerability check, Trivy | transitive packages are not reviewed by hand |
| Malicious or careless code change | CI, CodeQL, review of sensitive paths (CODEOWNERS), guard tests | see below |

## Automated checks (GitHub Actions)

- `CI`: build, unit tests, known-vulnerable NuGet and npm packages, secret scan (gitleaks), Trivy (dependencies, secrets,
  Dockerfile / chart misconfigurations).
- `CodeQL`: static analysis for C# and TypeScript, weekly and on each change.
- Dependabot: weekly update pull requests for NuGet, npm, Docker and GitHub Actions.
- `SecurityGuardTests`: fails if a new anonymous endpoint or a process start in the server modules appears without being
  added to the test on purpose.

## Verifying a release

Releases are built by GitHub Actions from the tagged commit. Each file has a signed build provenance (Sigstore); a software bill
of materials (CycloneDX) and `SHA256SUMS` are attached.

```bash
# the file really was built by this repository's workflow from the tagged commit
gh attestation verify <agent package from the release> --repo tschlaeppi/odm
sha256sum -c SHA256SUMS
```

## Settings the repository owner should enable on GitHub

Not part of the code, but they make the checks binding:

1. **Branch protection for `main`:** require a pull request, require the `CI` and `CodeQL` checks, require review from code
   owners, no force pushes.
2. Two-factor authentication on the account; signed commits ("Vigilant mode").
3. **Settings → Actions → General:** workflow permissions "read repository contents" by default; do not run workflows from
   fork pull requests with secrets.
4. **Security tab:** enable private vulnerability reporting, Dependabot alerts and secret scanning with push protection.
5. Optionally the public OpenSSF Scorecard (`ossf/scorecard-action`).

## Reviewing changes (including AI-written ones)

Read these paths line by line; the rest is UI and CRUD: authentication and policies (`Modules/Identity`), agent update and job
execution (`src/Agent`, `Modules/Agents`), install scripts and `scripts/`, `Dockerfile`, `deploy/`, `.github/`. Look for new
outbound hosts, process starts, new dependencies and changes to authorization. Run a new version once in a container without
internet access (or with an egress allow list) and look at the connections it tries to open.

## Known gaps

- Agent updates and PSADT / repository packages are not signed (see above). Planned: sign agent packages with a key that is
  not on the server and let the agent verify the signature.
- GitHub Actions are referenced by version tag, not by commit SHA; Dependabot keeps the versions current.
- No external penetration test.
