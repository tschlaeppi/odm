<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type TokenCreatedDto, type TokenDto } from './api'
  import { i18n, type MessageKey } from './i18n.svelte'

  const validities = [1, 24, 72, 168]

  let tokens = $state<TokenDto[]>([])
  let loading = $state(true)
  let failed = $state(false)

  let label = $state('')
  let validHours = $state(24)
  let busy = $state(false)
  let errorKey = $state<MessageKey | null>(null)

  let created = $state<TokenCreatedDto | null>(null)
  let copied = $state(false)

  async function load() {
    try {
      tokens = await api.registrationTokens()
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
    errorKey = null
    copied = false
    try {
      created = await api.createRegistrationToken(label.trim(), validHours)
      label = ''
      await load()
    } catch {
      errorKey = 'common.saveFailed'
    } finally {
      busy = false
    }
  }

  async function copy() {
    if (!created) return
    await navigator.clipboard.writeText(created.token)
    copied = true
  }

  async function remove(token: TokenDto) {
    try {
      await api.deleteRegistrationToken(token.id)
      tokens = tokens.filter((t) => t.id !== token.id)
    } catch {
      errorKey = 'common.saveFailed'
    }
  }
</script>

<section>
  <h1>{i18n.t('enroll.title')}</h1>
  <p class="muted intro">{i18n.t('enroll.intro')}</p>

  <form class="card form" onsubmit={create}>
    <div class="row">
      <label class="grow">
        <span>{i18n.t('enroll.label')}</span>
        <input bind:value={label} required maxlength="128" />
      </label>
      <label>
        <span>{i18n.t('enroll.validity')}</span>
        <select bind:value={validHours}>
          {#each validities as hours}
            <option value={hours}>{i18n.t(`enroll.h${hours}` as MessageKey)}</option>
          {/each}
        </select>
      </label>
      <button class="primary" type="submit" disabled={busy}>{i18n.t('enroll.create')}</button>
    </div>

    <p class="muted small">{i18n.t('enroll.serverUrl')}: <code>{location.origin}</code></p>

    {#if errorKey}
      <p class="error" role="alert">{i18n.t(errorKey)}</p>
    {/if}
  </form>

  {#if created}
    <div class="card created">
      <strong>{i18n.t('enroll.created')}: {created.label}</strong>
      <code class="token">{created.token}</code>
      <div class="row">
        <button onclick={copy}>{copied ? i18n.t('enroll.copied') : i18n.t('enroll.copy')}</button>
        <span class="muted small">{i18n.t('enroll.once')}</span>
      </div>
    </div>
  {/if}

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if failed}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else if tokens.length === 0}
    <p class="muted">{i18n.t('enroll.empty')}</p>
  {:else}
    <div class="card list">
      <table>
        <thead>
          <tr>
            <th>{i18n.t('groups.name')}</th>
            <th>{i18n.t('enroll.status')}</th>
            <th>{i18n.t('enroll.createdAt')}</th>
            <th>{i18n.t('enroll.expires')}</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {#each tokens as token (token.id)}
            <tr>
              <td class="strong">{token.label}</td>
              <td>{i18n.t(`enroll.status.${token.status}` as MessageKey)}</td>
              <td>{i18n.formatDate(token.createdAt)}</td>
              <td>{i18n.formatDate(token.expiresAt)}</td>
              <td class="actions">
                <button class="ghost" onclick={() => remove(token)}>{i18n.t('common.delete')}</button>
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
    margin: 0 0 0.5rem;
  }
  .intro {
    margin: 0 0 1rem;
    max-width: 46rem;
  }
  .form,
  .created {
    display: grid;
    gap: 0.75rem;
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
  code {
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  }
  .token {
    display: block;
    padding: 0.6rem 0.8rem;
    background: var(--bg);
    border: 1px solid var(--border);
    border-radius: 0.45rem;
    word-break: break-all;
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
  .actions {
    text-align: right;
  }
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
