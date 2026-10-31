<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type AuditEntryDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  const outcomes = ['success', 'failed', 'denied', 'error']

  let items = $state<AuditEntryDto[]>([])
  let total = $state(0)
  let loading = $state(true)
  let failed = $state(false)

  let actor = $state('')
  let text = $state('')
  let outcome = $state('')
  let from = $state('')

  async function load() {
    loading = true
    try {
      const page = await api.audit({
        actor: actor.trim(),
        text: text.trim(),
        outcome,
        from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined,
      })
      items = page.items
      total = page.total
      failed = false
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  onMount(load)

  function submit(event: SubmitEvent) {
    event.preventDefault()
    load()
  }

  function actionLabel(action: string) {
    const key = `audit.action.${action}` as MessageKey
    const label = i18n.t(key)
    return label === key ? action : label
  }

  function badge(entry: AuditEntryDto) {
    return entry.outcome === 'success' ? 'ok' : entry.outcome === 'denied' ? 'run' : 'err'
  }

  const columns = $derived<Column<AuditEntryDto>[]>([
    { key: 'time', label: i18n.t('audit.time'), value: (e) => e.timestamp, cell: timeCell, filter: 'none' },
    { key: 'actor', label: i18n.t('audit.actor'), value: (e) => e.actor, strong: true },
    { key: 'action', label: i18n.t('audit.action'), value: (e) => actionLabel(e.action) },
    { key: 'target', label: i18n.t('audit.target'), value: (e) => e.target, muted: true },
    { key: 'outcome', label: i18n.t('audit.outcome'), value: (e) => i18n.t(`audit.outcome.${e.outcome}` as MessageKey), cell: outcomeCell, filter: 'select' },
    { key: 'ip', label: i18n.t('audit.ip'), value: (e) => e.ipAddress, muted: true },
  ])
</script>

{#snippet timeCell(e: AuditEntryDto)}
  {i18n.formatDate(e.timestamp)}
{/snippet}

{#snippet outcomeCell(e: AuditEntryDto)}
  <span class="badge {badge(e)}" title={e.details ?? undefined}>{i18n.t(`audit.outcome.${e.outcome}` as MessageKey)}</span>
{/snippet}

<section>
  <h1>{i18n.t('audit.title')}</h1>
  <p class="muted intro">{i18n.t('audit.intro')}</p>

  <form class="card filters" onsubmit={submit}>
    <label>
      <span>{i18n.t('audit.actor')}</span>
      <input bind:value={actor} maxlength="128" />
    </label>
    <label class="grow">
      <span>{i18n.t('audit.text')}</span>
      <input bind:value={text} maxlength="128" />
    </label>
    <label>
      <span>{i18n.t('audit.outcome')}</span>
      <select bind:value={outcome}>
        <option value="">{i18n.t('audit.all')}</option>
        {#each outcomes as o}
          <option value={o}>{i18n.t(`audit.outcome.${o}` as MessageKey)}</option>
        {/each}
      </select>
    </label>
    <label>
      <span>{i18n.t('audit.from')}</span>
      <input type="date" bind:value={from} />
    </label>
    <button type="submit" disabled={loading}>{i18n.t('audit.search')}</button>
  </form>

  {#if failed}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else}
    {#if total > items.length}
      <p class="muted">{i18n.t('audit.showing')} {items.length} {i18n.t('audit.of')} {total}</p>
    {/if}
    <DataTable
      rows={items}
      {columns}
      rowKey={(e) => e.id}
      stale={loading}
      emptyText={i18n.t('audit.empty')}
      storageKey="audit"
    />
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
  .filters {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: flex-end;
    margin-bottom: 1rem;
  }
  .filters label {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    font-size: 0.85rem;
  }
  .filters .grow {
    flex: 1 1 14rem;
  }
</style>
