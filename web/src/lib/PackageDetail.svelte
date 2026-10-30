<script lang="ts">
  import { onDestroy, onMount } from 'svelte'
  import {
    api,
    ApiError,
    uploadVersion,
    type AssignmentDto,
    type AssignmentIntent,
    type DeviceDto,
    type GroupDto,
    type InstallationDto,
    type PackageDetailDto,
    type Rollout,
    type TargetKind,
    type VersionDto,
    type VersionPolicy,
  } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { formatSize } from './format'
  import { i18n, type MessageKey } from './i18n.svelte'
  import MessageDialog from './MessageDialog.svelte'
  import PackageSpecForm from './PackageSpecForm.svelte'
  import RolloutEditor from './RolloutEditor.svelte'
  import { router } from './router.svelte'
  import { emptySpec, specFromDto, specToDto } from './spec'

  let { id }: { id: string } = $props()

  let messageDialog: MessageDialog
  let pkgName = $state('')
  let intent = $state<AssignmentIntent>('Install')
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

  const statusClass = { Pending: '', Running: 'run', Installed: 'ok', Failed: 'err', Removed: '' } as const

  async function loadPackage() {
    try {
      pkg = await api.package(id)
      spec = specFromDto(pkg.spec)
      pkgName = pkg.name
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
        pinnedVersionId: intent === 'Install' && policy === 'Pinned' ? pinnedId || null : null,
        rollout: intent === 'Install' ? rollout : null,
        intent,
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
      await api.updatePackageAssignment(assignment.id, newPolicy, pin, assignment.rollout, assignment.intent)
    } catch {
      /* Anzeige wird unten neu geladen */
    }
    await loadAssignments()
  }

  async function changeIntent(assignment: AssignmentDto, newIntent: AssignmentIntent) {
    try {
      await api.updatePackageAssignment(
        assignment.id, assignment.versionPolicy, assignment.pinnedVersionId, assignment.rollout, newIntent,
      )
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
      await api.updatePackage(id, pkg.description, specToDto(spec), pkgName.trim())
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
  const versionColumns = $derived<Column<VersionDto>[]>([
    { key: 'version', label: i18n.t('sw.version'), value: (v) => v.version, strong: true },
    ...(pkg?.kind === 'Psadt'
      ? [{ key: 'size', label: i18n.t('sw.size'), value: (v: VersionDto) => v.sizeBytes, cell: versionSize, align: 'right' as const, filter: 'none' as const }]
      : []),
    { key: 'uploaded', label: i18n.t('sw.uploadedAt'), value: (v) => v.uploadedAt, cell: versionDate, filter: 'none' },
    ...(pkg?.kind === 'Psadt'
      ? [{ key: 'sha', label: 'SHA-256', value: (v: VersionDto) => v.sha256, cell: versionSha }]
      : []),
    { key: 'notes', label: i18n.t('sw.notes'), value: (v) => v.notes, muted: true },
  ])

  const assignmentColumns = $derived<Column<AssignmentDto>[]>([
    { key: 'kind', label: i18n.t('sw.targetKind'), value: (a) => i18n.t(`sw.targetKind.${a.targetKind}` as MessageKey), filter: 'select' },
    { key: 'target', label: i18n.t('sw.name'), value: (a) => targetLabel(a), strong: true },
    { key: 'intent', label: i18n.t('sw.intent'), value: (a) => i18n.t(`sw.intent.${a.intent}` as MessageKey), cell: intentCell, filter: 'select' },
    { key: 'policy', label: i18n.t('sw.policy'), value: (a) => (a.intent === 'Uninstall' ? '–' : i18n.t(`sw.policy.${a.versionPolicy}` as MessageKey)), cell: policyCell, filter: 'select' },
    { key: 'rollout', label: i18n.t('rollout.column'), value: (a) => rolloutText(a) },
  ])

  const installationColumns = $derived<Column<InstallationDto>[]>([
    { key: 'device', label: i18n.t('devices.name'), value: (i) => i.deviceName ?? i.deviceId, cell: installationDevice, strong: true },
    { key: 'target', label: i18n.t('sw.targetVersion'), value: (i) => i.targetVersion },
    { key: 'installed', label: i18n.t('sw.installedVersion'), value: (i) => i.installedVersion },
    { key: 'status', label: i18n.t('sw.status'), value: (i) => i18n.t(`sw.status.${i.status}` as MessageKey), cell: installationStatus, filter: 'select' },
    { key: 'attempts', label: i18n.t('sw.attempts'), value: (i) => i.attempts, align: 'right', filter: 'none' },
    { key: 'message', label: i18n.t('dev.detail.message'), value: (i) => i.message, cell: installationMessage, muted: true },
  ])
</script>

{#snippet versionSize(v: VersionDto)}{formatSize(v.sizeBytes)}{/snippet}
{#snippet versionDate(v: VersionDto)}{i18n.formatDate(v.uploadedAt)}{/snippet}
{#snippet versionSha(v: VersionDto)}<code title={v.sha256 ?? ''}>{(v.sha256 ?? '').slice(0, 12)}…</code>{/snippet}

{#snippet intentCell(a: AssignmentDto)}
  {#if auth.canEdit}
    <select value={a.intent} onchange={(e) => changeIntent(a, e.currentTarget.value as AssignmentIntent)}>
      <option value="Install">{i18n.t('sw.intent.Install')}</option>
      <option value="Uninstall">{i18n.t('sw.intent.Uninstall')}</option>
    </select>
  {:else}
    {i18n.t(`sw.intent.${a.intent}` as MessageKey)}
  {/if}
{/snippet}

{#snippet policyCell(a: AssignmentDto)}
  {#if a.intent === 'Uninstall'}
    –
  {:else if auth.canEdit && pkg?.kind === 'Psadt'}
    <select
      value={a.versionPolicy}
      onchange={(e) => changePolicy(a, e.currentTarget.value as VersionPolicy, a.pinnedVersionId)}
    >
      <option value="Latest">{i18n.t('sw.policy.Latest')}</option>
      <option value="Pinned">{i18n.t('sw.policy.Pinned')}</option>
    </select>
    {#if a.versionPolicy === 'Pinned'}
      <select value={a.pinnedVersionId} onchange={(e) => changePolicy(a, 'Pinned', e.currentTarget.value)}>
        {#each pkg.versions as v (v.id)}
          <option value={v.id}>{v.version}</option>
        {/each}
      </select>
    {/if}
  {:else}
    {i18n.t(`sw.policy.${a.versionPolicy}` as MessageKey)}
  {/if}
{/snippet}

{#snippet assignmentActions(a: AssignmentDto)}
  <button class="ghost" onclick={() => removeAssignment(a)}>{i18n.t('sw.removeAssignment')}</button>
{/snippet}

{#snippet installationDevice(i: InstallationDto)}
  <a href={`#/devices/${i.deviceId}`}>{i.deviceName ?? i.deviceId}</a>
{/snippet}

{#snippet installationStatus(i: InstallationDto)}
  <span class="badge {statusClass[i.status]}">{i.removal && (i.status === 'Pending' || i.status === 'Running' || i.status === 'Failed') ? i18n.t(`sw.removal.${i.status}` as MessageKey) : i18n.t(`sw.status.${i.status}` as MessageKey)}</span>
{/snippet}

{#snippet installationMessage(i: InstallationDto)}
  <span class="msg" title={i.message ?? ''}>{i.message ?? '–'}</span>
{/snippet}

{#snippet installationActions(i: InstallationDto)}
  {#if i.message}
    <button class="ghost" onclick={() => messageDialog.show(`${i.deviceName ?? i.deviceId} – ${i18n.t('msg.title')}`, i.message)}>
      {i18n.t('msg.show')}
    </button>
  {/if}
  {#if auth.canEdit && i.status === 'Failed'}
    <button class="ghost" onclick={() => retry(i)}>{i18n.t('sw.retry')}</button>
  {/if}
{/snippet}

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
        <label>
          <span>{i18n.t('sw.name.label')}</span>
          <input bind:value={pkgName} required maxlength="128" />
        </label>
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
      <DataTable rows={pkg.versions} columns={versionColumns} rowKey={(v) => v.id} pageSize={25} />
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

          <label>
            <span>{i18n.t('sw.intent')}</span>
            <select bind:value={intent}>
              <option value="Install">{i18n.t('sw.intent.Install')}</option>
              <option value="Uninstall">{i18n.t('sw.intent.Uninstall')}</option>
            </select>
          </label>

          {#if pkg.kind === 'Psadt' && intent === 'Install'}
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
        {#if intent === 'Install'}
          <RolloutEditor bind:value={rollout} {groups} />
        {/if}
        <p class="muted small">{intent === 'Uninstall' ? i18n.t('sw.intentHint') : i18n.t('sw.assignmentHint')}</p>
        {#if assignErrorKey}<p class="error" role="alert">{i18n.t(assignErrorKey)}</p>{/if}
      </form>
    {/if}

    {#if assignments.length === 0}
      <p class="muted">{i18n.t('sw.noAssignments')}</p>
    {:else}
      <DataTable
        rows={assignments}
        columns={assignmentColumns}
        rowKey={(a) => a.id}
        actions={auth.canEdit ? assignmentActions : undefined}
        pageSize={25}
      />
    {/if}

    <!-- Installationen -->
    <h2>{i18n.t('sw.installations')}</h2>

    {#if installations.length === 0}
      <p class="muted">{i18n.t('sw.noInstallations')}</p>
    {:else}
      <DataTable
        rows={installations}
        columns={installationColumns}
        rowKey={(i) => i.id}
        actions={installationActions}
      />
    {/if}
  {/if}
</section>

<MessageDialog bind:this={messageDialog} />

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
  .msg {
    display: inline-block;
    max-width: 24rem;
    overflow: hidden;
    text-overflow: ellipsis;
    vertical-align: bottom;
  }
</style>
