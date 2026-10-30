<script lang="ts">
  import { onDestroy, onMount } from 'svelte'
  import {
    api,
    ApiError,
    uploadVersion,
    type AssignmentDto,
    type DeviceDto,
    type GroupDto,
    type InstallationDto,
    type PackageDetailDto,
    type Rollout,
    type TargetKind,
    type VersionPolicy,
  } from './api'
  import { auth } from './auth.svelte'
  import { formatSize } from './format'
  import { i18n, type MessageKey } from './i18n.svelte'
  import PackageSpecForm from './PackageSpecForm.svelte'
  import RolloutEditor from './RolloutEditor.svelte'
  import { router } from './router.svelte'
  import { emptySpec, specFromDto, specToDto } from './spec'

  let { id }: { id: string } = $props()

  let pkg = $state<PackageDetailDto | null>(null)
  let assignments = $state<AssignmentDto[]>([])
  let installations = $state<InstallationDto[]>([])
  let groups = $state<GroupDto[]>([])
  let notFound = $state(false)
  let loading = $state(true)
  let timer: ReturnType<typeof setInterval> | undefined

  // Einstellungen (Zusatzargumente; bei Repo-Paketen auch Bezeichner, Version usw.)
  let spec = $state(emptySpec('Psadt'))
  let secretNames = $state<string[]>([])
  let configMessage = $state<string | null>(null)
  let configError = $state<string | null>(null)

  // Upload
  let version = $state('')
  let notes = $state('')
  let files = $state<FileList | null>(null)
  let progress = $state<number | null>(null)
  let uploadError = $state<string | null>(null)

  // Neue Zuweisung
  let targetKind = $state<TargetKind>('Group')
  let groupId = $state('')
  let userName = $state('')
  let deviceSearch = $state('')
  let deviceResults = $state<DeviceDto[]>([])
  let deviceId = $state('')
  let policy = $state<VersionPolicy>('Latest')
  let pinnedId = $state('')
  let rollout = $state<Rollout | null>(null)
  let assignErrorKey = $state<MessageKey | null>(null)

  const statusClass = { Pending: '', Running: 'run', Installed: 'ok', Failed: 'err' } as const

  async function loadPackage() {
    try {
      pkg = await api.package(id)
      spec = specFromDto(pkg.spec)
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) notFound = true
    }
  }

  async function loadAssignments() {
    try {
      assignments = await api.assignments(id)
    } catch {
      /* wird beim nächsten Aufruf erneut versucht */
    }
  }

  async function loadInstallations() {
    try {
      installations = await api.installations(id)
    } catch {
      /* wird beim nächsten Aufruf erneut versucht */
    }
  }

  onMount(async () => {
    await Promise.all([loadPackage(), loadAssignments(), loadInstallations()])
    try {
      groups = await api.groups()
    } catch {
      groups = []
    }
    try {
      secretNames = (await api.secrets()).map((s) => s.name)
    } catch {
      secretNames = []
    }
    loading = false
    timer = setInterval(loadInstallations, 10000)
  })

  onDestroy(() => clearInterval(timer))

  // Gerätesuche für die Zuweisung (verzögert)
  $effect(() => {
    const term = deviceSearch.trim()
    if (targetKind !== 'Device' || term.length < 2) {
      deviceResults = []
      return
    }
    const handle = setTimeout(async () => {
      try {
        deviceResults = (await api.devices({ search: term, take: 20 })).items
      } catch {
        deviceResults = []
      }
    }, 250)
    return () => clearTimeout(handle)
  })

  async function upload(event: SubmitEvent) {
    event.preventDefault()
    const file = files?.[0]
    if (!file) return
    uploadError = null
    progress = 0
    try {
      await uploadVersion(id, version.trim(), notes.trim(), file, (fraction) => (progress = fraction))
      version = ''
      notes = ''
      files = null
      await loadPackage()
      await loadInstallations()
    } catch (e) {
      uploadError =
        e instanceof ApiError && e.status === 409
          ? i18n.t('sw.versionExists')
          : ((e instanceof ApiError && e.detail) || i18n.t('sw.uploadFailed'))
    } finally {
      progress = null
    }
  }

  async function addAssignment(event: SubmitEvent) {
    event.preventDefault()
    assignErrorKey = null
    const target = targetKind === 'Group' ? groupId : targetKind === 'Device' ? deviceId : userName.trim()
    try {
      await api.createAssignment({
        packageId: id,
        targetKind,
        target,
        versionPolicy: policy,
        pinnedVersionId: policy === 'Pinned' ? pinnedId || null : null,
        rollout,
      })
      rollout = null
      userName = ''
      deviceSearch = ''
      deviceId = ''
      await loadAssignments()
    } catch (e) {
      assignErrorKey = e instanceof ApiError && e.status === 409 ? 'sw.assignExists' : 'sw.assignFailed'
    }
  }

  async function changePolicy(assignment: AssignmentDto, newPolicy: VersionPolicy, pinned: string | null) {
    const pin = newPolicy === 'Pinned' ? (pinned ?? pkg?.versions[0]?.id ?? null) : null
    try {
      await api.updatePackageAssignment(assignment.id, newPolicy, pin, assignment.rollout)
    } catch {
      /* Anzeige wird unten neu geladen */
    }
    await loadAssignments()
  }

  async function removeAssignment(assignment: AssignmentDto) {
    await api.deleteAssignment(assignment.id)
    await loadAssignments()
  }

  async function retry(installation: InstallationDto) {
    await api.retryInstallation(installation.id)
    await loadInstallations()
  }

  async function saveConfig(event: SubmitEvent) {
    event.preventDefault()
    if (!pkg) return
    configMessage = null
    configError = null
    try {
      await api.updatePackage(id, pkg.description, specToDto(spec))
      configMessage = i18n.t('sw.configSaved')
      await Promise.all([loadPackage(), loadInstallations()])
    } catch (e) {
      configError = (e instanceof ApiError && e.detail) || i18n.t('common.saveFailed')
    }
  }

  async function removePackage() {
    if (!pkg || !confirm(i18n.t('sw.confirmDelete', { name: pkg.name }))) return
    await api.deletePackage(id)
    router.navigate('/software')
  }

  function rolloutText(a: AssignmentDto): string {
    const status = a.rolloutStatus
    if (!status) return i18n.t('rollout.immediate')
    return status.releasedPercent === null
      ? i18n.t('rollout.status', { reached: status.stagesReached, total: status.stagesTotal })
      : i18n.t('rollout.statusPercent', {
          reached: status.stagesReached,
          total: status.stagesTotal,
          percent: status.releasedPercent,
        })
  }

  function targetLabel(a: AssignmentDto): string {
    if (a.targetKind === 'Group') return groups.find((g) => g.id === a.target)?.name ?? a.target
    if (a.targetKind === 'Device') return a.targetName ?? a.target
    return a.target
  }
</script>

<section>
  <a class="back" href="#/software">← {i18n.t('common.back')}</a>

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if notFound || !pkg}
    <p class="muted">{i18n.t('sw.notFound')}</p>
  {:else}
    <header class="head">
      <div>
        <h1>{pkg.name}</h1>
        <p class="muted">{pkg.description ?? i18n.t(`sw.kind.${pkg.kind}` as MessageKey)}</p>
      </div>
      {#if auth.canEdit}
        <button class="ghost" onclick={removePackage}>{i18n.t('common.delete')}</button>
      {/if}
    </header>

    <!-- Einstellungen -->
    <h2>{i18n.t('sw.config')}</h2>
    <form class="card form" onsubmit={saveConfig}>
      <fieldset disabled={!auth.canEdit}>
        {#key pkg.id}
          <PackageSpecForm kind={pkg.kind} bind:spec {secretNames} />
        {/key}
      </fieldset>
      {#if auth.canEdit}
        <div class="row">
          <button class="primary" type="submit">{i18n.t('sw.saveConfig')}</button>
          {#if configMessage}<span class="muted small">{configMessage}</span>{/if}
        </div>
      {/if}
      {#if configError}<p class="error" role="alert">{configError}</p>{/if}
    </form>

    <!-- Versionen bzw. Revisionen -->
    <h2>{pkg.kind === 'Psadt' ? i18n.t('sw.versions') : i18n.t('sw.revisions')}</h2>
    {#if pkg.kind !== 'Psadt'}
      <p class="muted small">{i18n.t('sw.revisionHint')}</p>
    {/if}

    {#if auth.canEdit && pkg.kind === 'Psadt'}
      <form class="card form" onsubmit={upload}>
        <div class="row">
          <label>
            <span>{i18n.t('sw.version')}</span>
            <input bind:value={version} required maxlength="64" placeholder="1.0.0" />
          </label>
          <label class="grow">
            <span>{i18n.t('sw.notes')}</span>
            <input bind:value={notes} maxlength="1024" />
          </label>
          <label class="grow">
            <span>{i18n.t('sw.file')}</span>
            <input type="file" accept=".zip" bind:files required />
          </label>
          <button class="primary" type="submit" disabled={progress !== null}>{i18n.t('sw.uploadBtn')}</button>
        </div>
        {#if progress !== null}
          <progress value={progress} max="1"></progress>
          <p class="muted small">{i18n.t('sw.uploading', { percent: Math.round(progress * 100) })}</p>
        {/if}
        {#if uploadError}<p class="error" role="alert">{uploadError}</p>{/if}
      </form>
    {/if}

    {#if pkg.versions.length === 0}
      <p class="muted">{i18n.t('sw.noVersions')}</p>
    {:else}
      <div class="card list">
        <table>
          <thead>
            <tr>
              <th>{i18n.t('sw.version')}</th>
              {#if pkg.kind === 'Psadt'}<th>{i18n.t('sw.size')}</th>{/if}
              <th>{i18n.t('sw.uploadedAt')}</th>
              {#if pkg.kind === 'Psadt'}<th>SHA-256</th>{/if}
              <th>{i18n.t('sw.notes')}</th>
            </tr>
          </thead>
          <tbody>
            {#each pkg.versions as v (v.id)}
              <tr>
                <td class="strong">{v.version}</td>
                {#if pkg.kind === 'Psadt'}<td>{formatSize(v.sizeBytes)}</td>{/if}
                <td>{i18n.formatDate(v.uploadedAt)}</td>
                {#if pkg.kind === 'Psadt'}
                  <td><code title={v.sha256 ?? ''}>{(v.sha256 ?? '').slice(0, 12)}…</code></td>
                {/if}
                <td class="muted">{v.notes ?? ''}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}

    <!-- Zuweisungen -->
    <h2>{i18n.t('sw.assignments')}</h2>

    {#if auth.canEdit}
      <form class="card form" onsubmit={addAssignment}>
        <div class="row">
          <label>
            <span>{i18n.t('sw.targetKind')}</span>
            <select bind:value={targetKind}>
              {#each ['Group', 'Device', 'User'] as kind}
                <option value={kind}>{i18n.t(`sw.targetKind.${kind}` as MessageKey)}</option>
              {/each}
            </select>
          </label>

          {#if targetKind === 'Group'}
            <label class="grow">
              <span>{i18n.t('sw.pickGroup')}</span>
              <select bind:value={groupId} required>
                <option value="" disabled>–</option>
                {#each groups as g (g.id)}
                  <option value={g.id}>{g.name} ({g.deviceCount})</option>
                {/each}
              </select>
            </label>
          {:else if targetKind === 'Device'}
            <label class="grow">
              <span>{i18n.t('sw.searchDevice')}</span>
              <input bind:value={deviceSearch} />
            </label>
            <label class="grow">
              <span>{i18n.t('sw.pickDevice')}</span>
              <select bind:value={deviceId} required>
                <option value="" disabled>–</option>
                {#each deviceResults as d (d.id)}
                  <option value={d.id}>{d.name}</option>
                {/each}
              </select>
            </label>
          {:else}
            <label class="grow">
              <span>{i18n.t('sw.userName')}</span>
              <input bind:value={userName} required maxlength="256" />
            </label>
          {/if}

          {#if pkg.kind === 'Psadt'}
          <label>
            <span>{i18n.t('sw.policy')}</span>
            <select bind:value={policy}>
              <option value="Latest">{i18n.t('sw.policy.Latest')}</option>
              <option value="Pinned">{i18n.t('sw.policy.Pinned')}</option>
            </select>
          </label>

          {#if policy === 'Pinned'}
            <label>
              <span>{i18n.t('sw.version')}</span>
              <select bind:value={pinnedId} required>
                <option value="" disabled>–</option>
                {#each pkg.versions as v (v.id)}
                  <option value={v.id}>{v.version}</option>
                {/each}
              </select>
            </label>
          {/if}

          {/if}

          <button class="primary" type="submit">{i18n.t('sw.assign')}</button>
        </div>
        <RolloutEditor bind:value={rollout} {groups} />
        <p class="muted small">{i18n.t('sw.assignmentHint')}</p>
        {#if assignErrorKey}<p class="error" role="alert">{i18n.t(assignErrorKey)}</p>{/if}
      </form>
    {/if}

    {#if assignments.length === 0}
      <p class="muted">{i18n.t('sw.noAssignments')}</p>
    {:else}
      <div class="card list">
        <table>
          <thead>
            <tr>
              <th>{i18n.t('sw.targetKind')}</th>
              <th>{i18n.t('sw.name')}</th>
              <th>{i18n.t('sw.policy')}</th>
              <th>{i18n.t('rollout.column')}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {#each assignments as a (a.id)}
              <tr>
                <td>{i18n.t(`sw.targetKind.${a.targetKind}` as MessageKey)}</td>
                <td class="strong">{targetLabel(a)}</td>
                <td>
                  {#if auth.canEdit && pkg.kind === 'Psadt'}
                    <select
                      value={a.versionPolicy}
                      onchange={(e) => changePolicy(a, e.currentTarget.value as VersionPolicy, a.pinnedVersionId)}
                    >
                      <option value="Latest">{i18n.t('sw.policy.Latest')}</option>
                      <option value="Pinned">{i18n.t('sw.policy.Pinned')}</option>
                    </select>
                    {#if a.versionPolicy === 'Pinned'}
                      <select
                        value={a.pinnedVersionId}
                        onchange={(e) => changePolicy(a, 'Pinned', e.currentTarget.value)}
                      >
                        {#each pkg.versions as v (v.id)}
                          <option value={v.id}>{v.version}</option>
                        {/each}
                      </select>
                    {/if}
                  {:else}
                    {i18n.t(`sw.policy.${a.versionPolicy}` as MessageKey)}
                  {/if}
                </td>
                <td>{rolloutText(a)}</td>
                <td class="actions">
                  {#if auth.canEdit}
                    <button class="ghost" onclick={() => removeAssignment(a)}>{i18n.t('sw.removeAssignment')}</button>
                  {/if}
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}

    <!-- Installationen -->
    <h2>{i18n.t('sw.installations')}</h2>

    {#if installations.length === 0}
      <p class="muted">{i18n.t('sw.noInstallations')}</p>
    {:else}
      <div class="card list">
        <table>
          <thead>
            <tr>
              <th>{i18n.t('devices.name')}</th>
              <th>{i18n.t('sw.targetVersion')}</th>
              <th>{i18n.t('sw.installedVersion')}</th>
              <th>{i18n.t('sw.status')}</th>
              <th>{i18n.t('sw.attempts')}</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {#each installations as i (i.id)}
              <tr>
                <td class="strong">{i.deviceName ?? i.deviceId}</td>
                <td>{i.targetVersion ?? '–'}</td>
                <td>{i.installedVersion ?? '–'}</td>
                <td>
                  <span class="badge {statusClass[i.status]}" title={i.message ?? ''}>
                    {i18n.t(`sw.status.${i.status}` as MessageKey)}
                  </span>
                </td>
                <td>{i.attempts}</td>
                <td class="actions">
                  {#if auth.canEdit && i.status === 'Failed'}
                    <button class="ghost" onclick={() => retry(i)}>{i18n.t('sw.retry')}</button>
                  {/if}
                </td>
              </tr>
              {#if i.status === 'Failed' && i.message}
                <tr class="detail"><td colspan="6"><pre>{i.message}</pre></td></tr>
              {/if}
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  {/if}
</section>

<style>
  .back {
    color: var(--muted);
    text-decoration: none;
    font-size: 0.9rem;
  }
  .head {
    display: flex;
    justify-content: space-between;
    align-items: start;
    margin: 0.75rem 0 0.5rem;
  }
  h1 {
    font-size: 1.35rem;
    margin: 0;
  }
  h2 {
    font-size: 1.05rem;
    margin: 1.75rem 0 0.75rem;
  }
  .head p {
    margin: 0.2rem 0 0;
  }
  .form {
    display: grid;
    gap: 0.6rem;
    margin-bottom: 1rem;
  }
  .row {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: end;
  }
  .grow {
    flex: 1;
    min-width: 11rem;
  }
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  .small {
    font-size: 0.85rem;
    margin: 0;
  }
  .list {
    padding: 0;
    overflow-x: auto;
    margin-bottom: 0.5rem;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.92rem;
  }
  th,
  td {
    text-align: left;
    padding: 0.6rem 1rem;
    white-space: nowrap;
  }
  th {
    font-weight: 500;
    font-size: 0.8rem;
    color: var(--muted);
    border-bottom: 1px solid var(--border);
  }
  tbody tr + tr td {
    border-top: 1px solid var(--border);
  }
  tr.detail td {
    white-space: normal;
    padding-top: 0;
    border-top: none;
  }
  pre {
    margin: 0;
    max-height: 10rem;
    overflow: auto;
    font-size: 0.8rem;
    color: var(--muted);
    white-space: pre-wrap;
  }
  .strong {
    font-weight: 600;
  }
  .actions {
    text-align: right;
  }
  fieldset {
    border: none;
    padding: 0;
    margin: 0;
    min-width: 0;
  }
  code {
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    font-size: 0.85rem;
  }
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
