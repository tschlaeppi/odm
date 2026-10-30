<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type AgentPackagesDto, type TokenCreatedDto, type TokenDto } from './api'
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
  let copiedCommand = $state<string | null>(null)
  let packages = $state<AgentPackagesDto | null>(null)

  const hasWindows = $derived(packages?.packages.some((p) => p.platform.startsWith('win')) ?? false)
  const hasLinux = $derived(packages?.packages.some((p) => p.platform.startsWith('linux')) ?? false)
  const tokenText = $derived(created?.token ?? i18n.t('enroll.token.placeholder'))
  const windowsCommand = $derived(
    packages ? `& ([scriptblock]::Create((irm '${packages.serverUrl}/api/agent/install.ps1'))) -Token '${tokenText}'` : ''
  )
  const linuxCommand = $derived(
    packages ? `curl -fsSL '${packages.serverUrl}/api/agent/install.sh' | sudo sh -s -- --token '${tokenText}'` : ''
  )

  function size(bytes: number) {
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  }

  async function copyCommand(text: string) {
    await navigator.clipboard.writeText(text)
    copiedCommand = text
  }

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

  onMount(() => {
    load()
    api.agentPackages().then((p) => (packages = p)).catch(() => (packages = null))
  })

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

  {#if packages}
    <div class="card install">
      <strong>{i18n.t('enroll.install')}</strong>
      {#if packages.packages.length === 0}
        <p class="muted small">{i18n.t('enroll.noPackages')}</p>
      {:else}
        <p class="muted small">{i18n.t('enroll.installHint')}</p>
        {#each [{ show: hasWindows, label: 'enroll.windows', cmd: windowsCommand }, { show: hasLinux, label: 'enroll.linux', cmd: linuxCommand }] as item}
          {#if item.show}
            <div class="cmd">
              <span class="muted small">{i18n.t(item.label as MessageKey)}</span>
              <code class="token">{item.cmd}</code>
              <div>
                <button onclick={() => copyCommand(item.cmd)}>
                  {copiedCommand === item.cmd ? i18n.t('enroll.copied') : i18n.t('enroll.copy')}
                </button>
              </div>
            </div>
          {/if}
        {/each}

        <strong class="sub">{i18n.t('enroll.downloads')}</strong>
        <div class="list">
          <table>
            <thead>
              <tr>
                <th>{i18n.t('enroll.platform')}</th>
                <th>{i18n.t('enroll.version')}</th>
                <th>{i18n.t('enroll.size')}</th>
                <th>SHA-256</th>
              </tr>
            </thead>
            <tbody>
              {#each packages.packages as pkg (pkg.platform)}
                <tr>
                  <td><a href={pkg.url} download>{pkg.platform}</a></td>
                  <td>{pkg.version}</td>
                  <td>{size(pkg.size)}</td>
                  <td><code class="hash">{pkg.sha256}</code></td>
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
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
  .created,
  .install {
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
  .cmd {
    display: grid;
    gap: 0.4rem;
  }
  .sub {
    margin-top: 0.5rem;
  }
  .hash {
    font-size: 0.75rem;
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
