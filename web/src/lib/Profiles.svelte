<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type PackageDto, type ProfileDto } from './api'
  import { auth } from './auth.svelte'
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
      // Ein Profil braucht mindestens ein Paket; weitere und die Reihenfolge folgen in der Detailansicht.
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
</script>

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
  {:else if profiles.length === 0}
    <p class="muted">{i18n.t('prof.empty')}</p>
  {:else}
    <div class="card list">
      <table>
        <thead>
          <tr>
            <th>{i18n.t('prof.name')}</th>
            <th>{i18n.t('prof.description')}</th>
            <th>{i18n.t('prof.items')}</th>
          </tr>
        </thead>
        <tbody>
          {#each profiles as p (p.id)}
            <tr>
              <td class="strong"><a href={`#/profiles/${p.id}`}>{p.name}</a></td>
              <td class="muted">{p.description ?? ''}</td>
              <td>{p.itemCount}</td>
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
    padding: 0.6rem 1rem;
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
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
