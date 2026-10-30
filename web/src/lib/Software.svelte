<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type PackageDto, type PackageKind, type SecretDto } from './api'
  import { auth } from './auth.svelte'
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
</script>

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
  {:else if packages.length === 0}
    <p class="muted">{i18n.t('sw.empty')}</p>
  {:else}
    <div class="card list">
      <table>
        <thead>
          <tr>
            <th>{i18n.t('sw.name')}</th>
            <th>{i18n.t('sw.kindLabel')}</th>
            <th>{i18n.t('sw.latest')}</th>
            <th>{i18n.t('sw.description')}</th>
          </tr>
        </thead>
        <tbody>
          {#each packages as pkg (pkg.id)}
            <tr>
              <td class="strong"><a href={`#/software/${pkg.id}`}>{pkg.name}</a></td>
              <td>{i18n.t(`sw.kind.${pkg.kind}` as MessageKey)}</td>
              <td>{pkg.latestVersion ?? '–'}</td>
              <td class="muted">{pkg.description ?? ''}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
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

  {#if secrets.length === 0}
    <p class="muted">{i18n.t('sw.secrets.empty')}</p>
  {:else}
    <div class="card list">
      <table>
        <thead>
          <tr>
            <th>{i18n.t('sw.secrets.name')}</th>
            <th>{i18n.t('sw.secrets.updated')}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {#each secrets as secret (secret.name)}
            <tr>
              <td class="strong"><code>{`{{secret:${secret.name}}}`}</code></td>
              <td>{i18n.formatDate(secret.updatedAt)}</td>
              <td class="actions">
                {#if isAdmin}
                  <button class="ghost" onclick={() => removeSecret(secret)}>{i18n.t('common.delete')}</button>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
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
  .actions {
    text-align: right;
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
  .list {
    padding: 0;
    overflow-x: auto;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.92rem;
  }
  th,
  td {
    text-align: left;
    padding: 0.7rem 1rem;
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
  .strong {
    font-weight: 600;
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
