<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type PackageDto, type ProfileDto } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n } from './i18n.svelte'
  import { router } from './router.svelte'

  let profiles = $state<ProfileDto[]>([])
  let packages = $state<PackageDto[]>([])
  let loading = $state(true)
  let failed = $state(false)

  let name = $state('')
  let description = $state('')
  let firstPackage = $state('')
  let busy = $state(false)
  let errorText = $state<string | null>(null)

  async function load() {
    try {
      ;[profiles, packages] = await Promise.all([api.profiles(), api.packages()])
      failed = false
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  onMount(load)

  async function create(event: SubmitEvent) {
    event.preventDefault()
    busy = true
    errorText = null
    try {
      // A profile needs at least one package; more and the order follow in the detail view.
      const created = await api.createProfile(name.trim(), description.trim() || null, [
        { packageId: firstPackage, versionPolicy: 'Latest', pinnedVersionId: null },
      ])
      router.navigate(`/profiles/${created.id}`)
    } catch (e) {
      errorText =
        e instanceof ApiError && e.status === 409
          ? i18n.t('prof.nameTaken')
          : ((e instanceof ApiError && e.detail) || i18n.t('common.saveFailed'))
    } finally {
      busy = false
    }
  }

  const columns = $derived<Column<ProfileDto>[]>([
    { key: 'name', label: i18n.t('prof.name'), value: (p) => p.name, cell: nameCell, strong: true },
    { key: 'desc', label: i18n.t('prof.description'), value: (p) => p.description, muted: true },
    { key: 'items', label: i18n.t('prof.items'), value: (p) => p.itemCount, align: 'right', filter: 'none' },
  ])
</script>

{#snippet nameCell(p: ProfileDto)}
  <a href={`#/profiles/${p.id}`}>{p.name}</a>
{/snippet}

<section>
  <h1>{i18n.t('prof.title')}</h1>
  <p class="muted">{i18n.t('prof.intro')}</p>

  {#if auth.canEdit}
    <form class="card form" onsubmit={create}>
      <div class="row">
        <label class="grow">
          <span>{i18n.t('prof.name')}</span>
          <input bind:value={name} required maxlength="128" />
        </label>
        <label class="grow">
          <span>{i18n.t('prof.description')}</span>
          <input bind:value={description} maxlength="1024" />
        </label>
        <label class="grow">
          <span>{i18n.t('prof.pickPackage')}</span>
          <select bind:value={firstPackage} required>
            <option value="" disabled>–</option>
            {#each packages as p (p.id)}
              <option value={p.id}>{p.name}</option>
            {/each}
          </select>
        </label>
        <button class="primary" type="submit" disabled={busy}>{i18n.t('prof.create')}</button>
      </div>
      {#if errorText}<p class="error" role="alert">{errorText}</p>{/if}
    </form>
  {/if}

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if failed}
    <p class="error">{i18n.t('prof.failedToLoad')}</p>
    <button onclick={load}>{i18n.t('devices.retry')}</button>
  {:else}
    <DataTable rows={profiles} {columns} rowKey={(p) => p.id} emptyText={i18n.t('prof.empty')} />
  {/if}
</section>

<style>
  h1 {
    font-size: 1.35rem;
    margin: 0 0 0.25rem;
  }
  .form {
    display: grid;
    gap: 0.6rem;
    margin: 1rem 0;
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
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
