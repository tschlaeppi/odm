<script lang="ts">
  import { onMount } from 'svelte'
  import {
    api,
    ApiError,
    type DeviceDto,
    type GroupDto,
    type PackageDto,
    type PackageKind,
    type ProfileAssignmentDto,
    type Rollout,
    type TargetKind,
    type VersionDto,
    type VersionPolicy,
  } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'
  import RolloutEditor from './RolloutEditor.svelte'
  import { router } from './router.svelte'

  let { id }: { id: string } = $props()

  interface Row {
    packageId: string
    name: string
    kind: PackageKind
    versionPolicy: VersionPolicy
    pinnedVersionId: string | null
  }

  let loading = $state(true)
  let notFound = $state(false)
  let name = $state('')
  let description = $state('')
  let rows = $state<Row[]>([])
  let packages = $state<PackageDto[]>([])
  let versions = $state<Record<string, VersionDto[]>>({})
  let groups = $state<GroupDto[]>([])
  let assignments = $state<ProfileAssignmentDto[]>([])
  let message = $state<string | null>(null)
  let errorText = $state<string | null>(null)
  let addPackageId = $state('')

  // Neue Zuweisung
  let targetKind = $state<TargetKind>('Group')
  let groupId = $state('')
  let userName = $state('')
  let deviceSearch = $state('')
  let deviceResults = $state<DeviceDto[]>([])
  let deviceId = $state('')
  let rollout = $state<Rollout | null>(null)
  let assignError = $state<MessageKey | null>(null)

  const available = $derived(packages.filter((p) => !rows.some((r) => r.packageId === p.id)))

  async function loadVersions(packageId: string) {
    if (versions[packageId]) return
    try {
      versions[packageId] = (await api.package(packageId)).versions
    } catch {
      versions[packageId] = []
    }
  }

  async function loadProfile() {
    try {
      const profile = await api.profile(id)
      name = profile.name
      description = profile.description ?? ''
      rows = profile.items.map((i) => ({
        packageId: i.packageId,
        name: i.packageName,
        kind: i.packageKind,
        versionPolicy: i.versionPolicy,
        pinnedVersionId: i.pinnedVersionId,
      }))
      await Promise.all(rows.filter((r) => r.kind === 'Psadt').map((r) => loadVersions(r.packageId)))
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) notFound = true
    }
  }

  async function loadAssignments() {
    try {
      assignments = await api.profileAssignments(id)
    } catch {
      /* retried on the next call */
    }
  }

  onMount(async () => {
    await Promise.all([loadProfile(), loadAssignments()])
    try {
      ;[packages, groups] = await Promise.all([api.packages(), api.groups()])
    } catch {
      packages = []
      groups = []
    }
    loading = false
  })

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

  function move(index: number, delta: -1 | 1) {
    const target = index + delta
    if (target < 0 || target >= rows.length) return
    const next = [...rows]
    ;[next[index], next[target]] = [next[target], next[index]]
    rows = next
  }

  function remove(index: number) {
    rows = rows.filter((_, i) => i !== index)
  }

  async function addItem() {
    const pkg = packages.find((p) => p.id === addPackageId)
    if (!pkg) return
    rows = [
      ...rows,
      { packageId: pkg.id, name: pkg.name, kind: pkg.kind, versionPolicy: 'Latest', pinnedVersionId: null },
    ]
    addPackageId = ''
    if (pkg.kind === 'Psadt') await loadVersions(pkg.id)
  }

  function policyChanged(row: Row, policy: VersionPolicy) {
    row.versionPolicy = policy
    row.pinnedVersionId = policy === 'Pinned' ? (versions[row.packageId]?.[0]?.id ?? null) : null
  }

  async function save(event: SubmitEvent) {
    event.preventDefault()
    message = null
    errorText = null
    try {
      await api.updateProfile(
        id,
        description.trim() || null,
        rows.map((r) => ({
          packageId: r.packageId,
          versionPolicy: r.versionPolicy,
          pinnedVersionId: r.versionPolicy === 'Pinned' ? r.pinnedVersionId : null,
        })),
      )
      message = i18n.t('prof.saved')
    } catch (e) {
      errorText = (e instanceof ApiError && e.detail) || i18n.t('common.saveFailed')
    }
  }

  async function removeProfile() {
    if (!confirm(i18n.t('prof.confirmDelete', { name }))) return
    await api.deleteProfile(id)
    router.navigate('/profiles')
  }

  async function addAssignment(event: SubmitEvent) {
    event.preventDefault()
    assignError = null
    const target = targetKind === 'Group' ? groupId : targetKind === 'Device' ? deviceId : userName.trim()
    try {
      await api.createProfileAssignment(id, targetKind, target, rollout)
      rollout = null
      userName = ''
      deviceSearch = ''
      deviceId = ''
      await loadAssignments()
    } catch (e) {
      assignError = e instanceof ApiError && e.status === 409 ? 'sw.assignExists' : 'sw.assignFailed'
    }
  }

  async function removeAssignment(a: ProfileAssignmentDto) {
    await api.deleteProfileAssignment(a.id)
    await loadAssignments()
  }

  function targetLabel(a: ProfileAssignmentDto): string {
    if (a.targetKind === 'Group') return groups.find((g) => g.id === a.target)?.name ?? a.target
    if (a.targetKind === 'Device') return a.targetName ?? a.target
    return a.target
  }

  function rolloutText(a: ProfileAssignmentDto): string {
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

  const assignmentColumns = $derived<Column<(typeof assignments)[number]>[]>([
    { key: 'kind', label: i18n.t('sw.targetKind'), value: (a) => i18n.t(`sw.targetKind.${a.targetKind}` as MessageKey), filter: 'select' },
    { key: 'target', label: i18n.t('sw.name'), value: (a) => targetLabel(a), strong: true },
    { key: 'rollout', label: i18n.t('rollout.column'), value: (a) => rolloutText(a) },
  ])
</script>

{#snippet assignmentActions(a: (typeof assignments)[number])}
  <button class="ghost" onclick={() => removeAssignment(a)}>{i18n.t('sw.removeAssignment')}</button>
{/snippet}

<section>
  <a class="back" href="#/profiles">← {i18n.t('common.back')}</a>

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if notFound}
    <p class="muted">{i18n.t('prof.notFound')}</p>
  {:else}
    <div class="head">
      <h1>{name}</h1>
      {#if auth.canEdit}
        <button class="ghost" onclick={removeProfile}>{i18n.t('common.delete')}</button>
      {/if}
    </div>

    <form class="card form" onsubmit={save}>
      <label>
        <span>{i18n.t('prof.description')}</span>
        <input bind:value={description} maxlength="1024" disabled={!auth.canEdit} />
      </label>

      <h2>{i18n.t('prof.order')}</h2>

      {#if rows.length === 0}
        <p class="muted">{i18n.t('prof.noItems')}</p>
      {:else}
        <ol class="items">
          {#each rows as row, index (row.packageId)}
            <li>
              <span class="pos">{index + 1}</span>
              <span class="strong grow">{row.name}</span>

              {#if row.kind === 'Psadt' && auth.canEdit}
                <select
                  value={row.versionPolicy}
                  onchange={(e) => policyChanged(row, e.currentTarget.value as VersionPolicy)}
                  aria-label={i18n.t('sw.policy')}
                >
                  <option value="Latest">{i18n.t('sw.policy.Latest')}</option>
                  <option value="Pinned">{i18n.t('sw.policy.Pinned')}</option>
                </select>
                {#if row.versionPolicy === 'Pinned'}
                  <select bind:value={row.pinnedVersionId} required aria-label={i18n.t('sw.version')}>
                    {#each versions[row.packageId] ?? [] as v (v.id)}
                      <option value={v.id}>{v.version}</option>
                    {/each}
                  </select>
                {/if}
              {:else if row.kind === 'Psadt'}
                <span class="muted">{i18n.t(`sw.policy.${row.versionPolicy}` as MessageKey)}</span>
              {/if}

              {#if auth.canEdit}
                <button type="button" class="ghost" disabled={index === 0} onclick={() => move(index, -1)} aria-label={i18n.t('prof.moveUp')}>↑</button>
                <button type="button" class="ghost" disabled={index === rows.length - 1} onclick={() => move(index, 1)} aria-label={i18n.t('prof.moveDown')}>↓</button>
                <button type="button" class="ghost" disabled={rows.length <= 1} onclick={() => remove(index)}>{i18n.t('prof.removeItem')}</button>
              {/if}
            </li>
          {/each}
        </ol>
      {/if}

      {#if auth.canEdit}
        <div class="row">
          <label class="grow">
            <span>{i18n.t('prof.pickPackage')}</span>
            <select bind:value={addPackageId}>
              <option value="">–</option>
              {#each available as p (p.id)}
                <option value={p.id}>{p.name}</option>
              {/each}
            </select>
          </label>
          <button type="button" disabled={!addPackageId} onclick={addItem}>{i18n.t('prof.addPackage')}</button>
          <button class="primary" type="submit">{i18n.t('common.save')}</button>
        </div>
      {/if}
      {#if message}<p class="muted">{message}</p>{/if}
      {#if errorText}<p class="error" role="alert">{errorText}</p>{/if}
    </form>

    <h2>{i18n.t('prof.assignments')}</h2>

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

          <button class="primary" type="submit">{i18n.t('sw.assign')}</button>
        </div>
        <RolloutEditor bind:value={rollout} {groups} />
        {#if assignError}<p class="error" role="alert">{i18n.t(assignError)}</p>{/if}
      </form>
    {/if}

    {#if assignments.length === 0}
      <p class="muted">{i18n.t('prof.noAssignments')}</p>
    {:else}
      <DataTable
        rows={assignments}
        columns={assignmentColumns}
        rowKey={(a) => a.id}
        actions={auth.canEdit ? assignmentActions : undefined}
        pageSize={25}
      />
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
    margin: 1.25rem 0 0.75rem;
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
  .items {
    list-style: none;
    margin: 0;
    padding: 0;
    display: grid;
    gap: 0.4rem;
  }
  .items li {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    align-items: center;
  }
  .pos {
    width: 1.6rem;
    height: 1.6rem;
    border-radius: 50%;
    display: grid;
    place-items: center;
    background: var(--hover);
    font-size: 0.8rem;
    color: var(--muted);
  }
  .strong {
    font-weight: 600;
  }
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
