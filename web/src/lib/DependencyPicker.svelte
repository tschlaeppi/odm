<script lang="ts" module>
  /** Mirrors PackageDependency.MaxPerPackage on the server. */
  export const MAX_DEPENDENCIES = 20

  export interface DependencyOption {
    id: string
    name: string
    kind: PackageKind
  }
</script>

<script lang="ts">
  import type { PackageKind } from './api'
  import { i18n, type MessageKey } from './i18n.svelte'

  let {
    options,
    selected = $bindable([]),
  }: { options: DependencyOption[]; selected: string[] } = $props()

  let query = $state('')
  let kind = $state<PackageKind | ''>('')
  let onlySelected = $state(false)

  const byId = $derived(new Map(options.map((o) => [o.id, o])))
  const kinds = $derived([...new Set(options.map((o) => o.kind))].sort())
  const atLimit = $derived(selected.length >= MAX_DEPENDENCIES)

  const kindLabel = (k: PackageKind) => i18n.t(`sw.kind.${k}` as MessageKey)

  const matches = $derived.by(() => {
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean)
    const chosen = new Set(selected)
    return options
      .filter((o) => (!kind || o.kind === kind) && (!onlySelected || chosen.has(o.id)))
      .filter((o) => {
        const haystack = `${o.name} ${kindLabel(o.kind)}`.toLowerCase()
        return terms.every((t) => haystack.includes(t))
      })
      .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
  })

  function toggle(id: string, checked: boolean) {
    if (checked) {
      if (!selected.includes(id) && !atLimit) selected = [...selected, id]
    } else {
      selected = selected.filter((s) => s !== id)
    }
  }

  function reset() {
    query = ''
    kind = ''
    onlySelected = false
  }
</script>

<div class="picker">
  {#if selected.length > 0}
    <ul class="chips" aria-label={i18n.t('sw.deps.selected')}>
      {#each selected as sid (sid)}
        <li class="chip">
          <span>{byId.get(sid)?.name ?? sid}</span>
          <button
            type="button"
            class="remove"
            onclick={() => toggle(sid, false)}
            aria-label={i18n.t('sw.deps.remove', { name: byId.get(sid)?.name ?? sid })}
            title={i18n.t('sw.deps.remove', { name: byId.get(sid)?.name ?? sid })}>×</button
          >
        </li>
      {/each}
    </ul>
  {/if}

  <div class="filters">
    <input class="search" type="search" bind:value={query} placeholder={i18n.t('table.search')} aria-label={i18n.t('table.search')} />
    {#if kinds.length > 1}
      <select bind:value={kind} aria-label={i18n.t('sw.deps.kind')}>
        <option value="">{i18n.t('table.all')}</option>
        {#each kinds as k (k)}
          <option value={k}>{kindLabel(k)}</option>
        {/each}
      </select>
    {/if}
    <label class="only">
      <input type="checkbox" bind:checked={onlySelected} />
      {i18n.t('sw.deps.onlySelected')}
    </label>
    <span class="count muted" class:limit={atLimit}>
      {i18n.t('sw.deps.count', { count: selected.length, max: MAX_DEPENDENCIES })}
    </span>
  </div>

  <div class="list" role="group" aria-label={i18n.t('sw.deps')}>
    {#each matches as o (o.id)}
      {@const checked = selected.includes(o.id)}
      <label class="option" class:checked>
        <input
          type="checkbox"
          {checked}
          disabled={!checked && atLimit}
          onchange={(e) => toggle(o.id, e.currentTarget.checked)}
        />
        <span class="name">{o.name}</span>
        <span class="badge">{kindLabel(o.kind)}</span>
      </label>
    {:else}
      <p class="muted empty">
        {i18n.t('table.noMatch')}
        <button type="button" class="ghost" onclick={reset}>{i18n.t('table.reset')}</button>
      </p>
    {/each}
  </div>

  <p class="muted footer">
    {matches.length === options.length
      ? i18n.t('sw.deps.total', { total: options.length })
      : i18n.t('sw.deps.shown', { shown: matches.length, total: options.length })}
    {#if atLimit}· {i18n.t('sw.deps.limit', { max: MAX_DEPENDENCIES })}{/if}
  </p>
</div>

<style>
  .picker {
    display: grid;
    gap: 0.5rem;
    margin-top: 0.4rem;
  }
  .picker input[type='checkbox'] {
    min-height: 0;
    width: auto;
    padding: 0;
    margin: 0;
    flex: none;
  }
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
    list-style: none;
    margin: 0;
    padding: 0;
  }
  .chip {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    padding: 0.15rem 0.25rem 0.15rem 0.65rem;
    border-radius: 999px;
    background: var(--accent-soft);
    color: var(--text);
    font-size: 0.85rem;
  }
  .remove {
    border: none;
    background: transparent;
    color: var(--muted);
    cursor: pointer;
    font-size: 1rem;
    line-height: 1;
    padding: 0 0.3rem;
    min-height: 0;
    border-radius: 999px;
  }
  .remove:hover:not(:disabled) {
    color: var(--text);
    background: var(--hover);
  }
  .remove:disabled {
    display: none;
  }
  .filters {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem 0.75rem;
    align-items: center;
  }
  .search {
    flex: 1;
    min-width: 12rem;
  }
  .filters select {
    width: auto;
  }
  .only {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    font-size: 0.85rem;
    color: var(--muted);
    white-space: nowrap;
  }
  .count {
    margin-left: auto;
    font-size: 0.85rem;
    white-space: nowrap;
  }
  .count.limit {
    color: var(--danger);
  }
  .list {
    max-height: 18rem;
    overflow-y: auto;
    border: 1px solid var(--border);
    border-radius: 8px;
    background: var(--surface);
  }
  .option {
    display: flex;
    align-items: center;
    gap: 0.6rem;
    padding: 0.4rem 0.75rem;
    border-bottom: 1px solid var(--border);
    color: var(--text);
    font-size: 0.9rem;
    cursor: pointer;
  }
  .option:last-child {
    border-bottom: none;
  }
  .option:hover {
    background: var(--hover);
  }
  .option.checked .name {
    font-weight: 600;
  }
  .option:has(input:disabled) {
    cursor: default;
    opacity: 0.55;
  }
  .name {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .empty {
    margin: 0;
    padding: 0.75rem;
    font-size: 0.85rem;
  }
  .footer {
    margin: 0;
    font-size: 0.8rem;
  }
</style>
