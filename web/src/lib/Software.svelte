<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type PackageDto, type PackageKind, type SecretDto } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'
  import PackageSpecForm from './PackageSpecForm.svelte'
  import { router } from './router.svelte'
  import { allKinds, emptySpec, specToDto } from './spec'

  let packages = $state<PackageDto[]>([])
  let secrets = $state<SecretDto[]>([])
  let loading = $state(true)
  let failed = $state(false)

  let name = $state('')
  let description = $state('')
  let kind = $state<PackageKind>('Psadt')
  let spec = $state(emptySpec('Psadt'))
  let busy = $state(false)
  let errorText = $state<string | null>(null)

  let secretName = $state('')
  let secretValue = $state('')
  let secretError = $state<string | null>(null)

  const isAdmin = $derived(auth.user?.role === 'Admin')

  async function load() {
    try {
      ;[packages, secrets] = await Promise.all([api.packages(), api.secrets()])
      failed = false
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  onMount(load)

  function kindChanged() {
    spec = emptySpec(kind)
  }

  async function create(event: SubmitEvent) {
    event.preventDefault()
    busy = true
    errorText = null
    try {
      const created = await api.createPackage(name.trim(), description.trim() || null, kind, specToDto(spec))
      router.navigate(`/software/${created.id}`)
    } catch (e) {
      errorText =
        e instanceof ApiError && e.status === 409
          ? i18n.t('sw.nameTaken')
          : ((e instanceof ApiError && e.detail) || i18n.t('common.saveFailed'))
    } finally {
      busy = false
    }
  }

  async function saveSecret(event: SubmitEvent) {
    event.preventDefault()
    secretError = null
    try {
      await api.setSecret(secretName.trim(), secretValue)
      secretName = ''
      secretValue = ''
      secrets = await api.secrets()
    } catch (e) {
      secretError = (e instanceof ApiError && e.detail) || i18n.t('common.saveFailed')
    }
  }

  async function removeSecret(secret: SecretDto) {
    secretError = null
    try {
      await api.deleteSecret(secret.name)
      secrets = await api.secrets()
    } catch (e) {
      secretError = e instanceof ApiError && e.status === 409 ? i18n.t('sw.secrets.inUse') : i18n.t('common.saveFailed')
    }
  }

  const columns = $derived<Column<PackageDto>[]>([
    { key: 'name', label: i18n.t('sw.name'), value: (p) => p.name, cell: nameCell, strong: true },
    { key: 'kind', label: i18n.t('sw.kindLabel'), value: (p) => i18n.t(`sw.kind.${p.kind}` as MessageKey), filter: 'select' },
    { key: 'latest', label: i18n.t('sw.latest'), value: (p) => p.latestVersion },
    { key: 'desc', label: i18n.t('sw.description'), value: (p) => p.description, muted: true },
  ])

  const secretColumns = $derived<Column<SecretDto>[]>([
    { key: 'name', label: i18n.t('sw.secrets.name'), value: (x) => x.name, cell: secretLabel, strong: true },
    { key: 'updated', label: i18n.t('sw.secrets.updated'), value: (x) => x.updatedAt, cell: secretUpdated, filter: 'none' },
  ])
</script>

{#snippet nameCell(p: PackageDto)}
  <a href={`#/software/${p.id}`}>{p.name}</a>
{/snippet}

{#snippet secretLabel(x: SecretDto)}
  <code>{`{{secret:${x.name}}}`}</code>
{/snippet}

{#snippet secretUpdated(x: SecretDto)}
  {i18n.formatDate(x.updatedAt)}
{/snippet}

{#snippet secretActions(x: SecretDto)}
  <button class="ghost" onclick={() => removeSecret(x)}>{i18n.t('common.delete')}</button>
{/snippet}

<section>
  <h1>{i18n.t('sw.title')}</h1>

  {#if auth.canEdit}
    <form class="card form" onsubmit={create}>
      <div class="row">
        <label class="grow">
          <span>{i18n.t('sw.name')}</span>
          <input bind:value={name} required maxlength="128" />
        </label>
        <label>
          <span>{i18n.t('sw.kindLabel')}</span>
          <select bind:value={kind} onchange={kindChanged}>
            {#each allKinds as k}
              <option value={k}>{i18n.t(`sw.kind.${k}` as MessageKey)}</option>
            {/each}
          </select>
        </label>
        <label class="grow">
          <span>{i18n.t('sw.description')}</span>
          <input bind:value={description} maxlength="1024" />
        </label>
      </div>

      {#key kind}
        <PackageSpecForm {kind} bind:spec secretNames={secrets.map((s) => s.name)} />
      {/key}

      <div class="row">
        <button class="primary" type="submit" disabled={busy}>{i18n.t('sw.create')}</button>
      </div>
      {#if errorText}<p class="error" role="alert">{errorText}</p>{/if}
    </form>
  {/if}

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if failed}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else}
    <DataTable rows={packages} {columns} rowKey={(p) => p.id} emptyText={i18n.t('sw.empty')} />
  {/if}

  <h2>{i18n.t('sw.secrets')}</h2>
  <p class="muted small">{i18n.t('sw.secrets.intro')}</p>

  {#if isAdmin}
    <form class="card form" onsubmit={saveSecret}>
      <div class="row">
        <label class="grow">
          <span>{i18n.t('sw.secrets.name')}</span>
          <input bind:value={secretName} required maxlength="64" pattern="[A-Za-z0-9_.\-]+" autocomplete="off" />
        </label>
        <label class="grow">
          <span>{i18n.t('sw.secrets.value')}</span>
          <input type="password" bind:value={secretValue} required maxlength="4000" autocomplete="new-password" />
        </label>
        <button class="primary" type="submit">{i18n.t('sw.secrets.save')}</button>
      </div>
    </form>
  {:else}
    <p class="muted small">{i18n.t('sw.secrets.adminOnly')}</p>
  {/if}
  {#if secretError}<p class="error" role="alert">{secretError}</p>{/if}

  <DataTable
    rows={secrets}
    columns={secretColumns}
    rowKey={(x) => x.name}
    actions={isAdmin ? secretActions : undefined}
    emptyText={i18n.t('sw.secrets.empty')}
  />
</section>

<style>
  h1 {
    font-size: 1.35rem;
    margin: 0 0 1rem;
  }
  h2 {
    font-size: 1.05rem;
    margin: 1.75rem 0 0.5rem;
  }
  code {
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    font-size: 0.85rem;
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
    min-width: 12rem;
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
  a {
    color: var(--accent);
    text-decoration: none;
  }
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
