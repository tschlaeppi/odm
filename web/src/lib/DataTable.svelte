<script lang="ts" module>
  import type { Snippet } from 'svelte'

  export interface Column<R> {
    key: string
    label: string
    /** Value for sorting and filtering (display via <c>cell</c>, otherwise this value). */
    value: (row: R) => string | number | null | undefined
    cell?: Snippet<[R]>
    /** 'select' = dropdown of the values that occur, otherwise a text filter. */
    filter?: 'text' | 'select' | 'none'
    align?: 'right'
    muted?: boolean
    strong?: boolean
  }
</script>

<script lang="ts" generics="T">
  import { untrack } from 'svelte'
  import { i18n } from './i18n.svelte'

  interface Props {
    rows: T[]
    columns: Column<T>[]
    rowKey: (row: T) => string
    actions?: Snippet<[T]>
    searchPlaceholder?: string
    emptyText?: string
    stale?: boolean
    /** Additional controls to the right of the search. */
    toolbar?: Snippet
    pageSize?: number
    /** Columns (key) that are initially hidden. */
    defaultHidden?: string[]
    /** Key under which the column selection is remembered in the browser. */
    storageKey?: string
  }

  let { rows, columns, rowKey, actions, searchPlaceholder, emptyText, stale = false, toolbar, pageSize = 50, defaultHidden = [], storageKey }: Props = $props()

  let query = $state('')
  let filters = $state<Record<string, string>>({})
  let showFilters = $state(false)
  let sortKey = $state<string | null>(null)
  let sortDesc = $state(false)
  let page = $state(0)
  let size = $state(untrack(() => pageSize))

  // Hidden columns; the selection is remembered in the browser (if possible).
  function loadHidden(): string[] {
    try {
      const raw = storageKey ? localStorage.getItem(`odm.columns.${storageKey}`) : null
      if (raw) return JSON.parse(raw) as string[]
    } catch {
      // Storage not available: use the default
    }
    return [...defaultHidden]
  }

  let hidden = $state<string[]>(untrack(loadHidden))
  let menuOpen = $state(false)
  let menu = $state<HTMLElement>()

  function toggleColumn(key: string) {
    hidden = hidden.includes(key) ? hidden.filter((k) => k !== key) : [...hidden, key]
    try {
      if (storageKey) localStorage.setItem(`odm.columns.${storageKey}`, JSON.stringify(hidden))
    } catch {
      // storage not available
    }
  }

  const shown = $derived(columns.filter((c) => !hidden.includes(c.key)))

  const text = (v: string | number | null | undefined) => (v === null || v === undefined ? '' : String(v))

  const activeFilters = $derived(shown.filter((c) => (filters[c.key] ?? '') !== '').length)

  const filtered = $derived.by(() => {
    const q = query.trim().toLowerCase()
    return rows.filter((row) => {
      if (q && !columns.some((c) => text(c.value(row)).toLowerCase().includes(q))) return false
      for (const c of shown) {
        const f = filters[c.key]
        if (!f) continue
        const v = text(c.value(row))
        if (c.filter === 'select' ? v !== f : !v.toLowerCase().includes(f.toLowerCase())) return false
      }
      return true
    })
  })

  const sorted = $derived.by(() => {
    const column = shown.find((c) => c.key === sortKey)
    if (!column) return filtered
    const dir = sortDesc ? -1 : 1
    return [...filtered].sort((a, b) => {
      const x = column.value(a)
      const y = column.value(b)
      if (x == null || x === '') return y == null || y === '' ? 0 : 1
      if (y == null || y === '') return -1
      if (typeof x === 'number' && typeof y === 'number') return (x - y) * dir
      return text(x).localeCompare(text(y), undefined, { numeric: true, sensitivity: 'base' }) * dir
    })
  })

  const pages = $derived(Math.max(1, Math.ceil(sorted.length / size)))
  const current = $derived(Math.min(page, pages - 1))
  const visible = $derived(sorted.slice(current * size, current * size + size))

  // Changes to filter/sort jump back to page 1
  $effect(() => {
    query
    filters
    sortKey
    sortDesc
    size
    page = 0
  })

  function options(column: Column<T>): string[] {
    return [...new Set(rows.map((r) => text(column.value(r))).filter((v) => v !== ''))].sort((a, b) => a.localeCompare(b))
  }

  function sortBy(column: Column<T>) {
    if (sortKey !== column.key) {
      sortKey = column.key
      sortDesc = false
    } else if (!sortDesc) {
      sortDesc = true
    } else {
      sortKey = null
    }
  }

  function reset() {
    query = ''
    filters = {}
  }

  const from = $derived(sorted.length === 0 ? 0 : current * size + 1)
  const to = $derived(Math.min(sorted.length, current * size + size))
</script>

<svelte:window onclick={(e) => { if (menuOpen && menu && !menu.contains(e.target as Node)) menuOpen = false }} />

<div class="dt">
  <div class="bar">
    <input class="search" type="search" placeholder={searchPlaceholder ?? i18n.t('table.search')} bind:value={query} />
    <button class:on={showFilters || activeFilters > 0} onclick={() => (showFilters = !showFilters)} aria-pressed={showFilters}>
      {i18n.t('table.filters')}{activeFilters > 0 ? ` (${activeFilters})` : ''}
    </button>
    {#if query || activeFilters > 0}
      <button class="ghost" onclick={reset}>{i18n.t('table.reset')}</button>
    {/if}
    <span class="grow"></span>
    {@render toolbar?.()}
    <div class="cols" bind:this={menu}>
      <button class:on={menuOpen} onclick={() => (menuOpen = !menuOpen)} aria-expanded={menuOpen}>
        {i18n.t('table.columns')}
      </button>
      {#if menuOpen}
        <div class="menu" role="group" aria-label={i18n.t('table.columns')}>
          {#each columns as column (column.key)}
            <label>
              <input type="checkbox" checked={!hidden.includes(column.key)} onchange={() => toggleColumn(column.key)} />
              {column.label}
            </label>
          {/each}
        </div>
      {/if}
    </div>
  </div>

  <div class="wrap" class:stale>
    <table>
      <thead>
        <tr>
          {#each shown as column (column.key)}
            <th class:right={column.align === 'right'} aria-sort={sortKey === column.key ? (sortDesc ? 'descending' : 'ascending') : 'none'}>
              <button class="sort" onclick={() => sortBy(column)}>
                {column.label}
                <span class="arrow">{sortKey === column.key ? (sortDesc ? '▼' : '▲') : ''}</span>
              </button>
            </th>
          {/each}
          {#if actions}<th></th>{/if}
        </tr>
        {#if showFilters}
          <tr class="filters">
            {#each shown as column (column.key)}
              <th>
                {#if column.filter === 'select'}
                  <select bind:value={filters[column.key]} aria-label={column.label}>
                    <option value="">{i18n.t('table.all')}</option>
                    {#each options(column) as option}
                      <option value={option}>{option}</option>
                    {/each}
                  </select>
                {:else if column.filter !== 'none'}
                  <input bind:value={filters[column.key]} placeholder={i18n.t('table.contains')} aria-label={column.label} />
                {/if}
              </th>
            {/each}
            {#if actions}<th></th>{/if}
          </tr>
        {/if}
      </thead>
      <tbody>
        {#each visible as row (rowKey(row))}
          <tr>
            {#each shown as column (column.key)}
              <td class:right={column.align === 'right'} class:muted={column.muted} class:strong={column.strong}>
                {#if column.cell}
                  {@render column.cell(row)}
                {:else}
                  {text(column.value(row)) || '–'}
                {/if}
              </td>
            {/each}
            {#if actions}<td class="actions">{@render actions(row)}</td>{/if}
          </tr>
        {:else}
          <tr>
            <td class="empty" colspan={shown.length + (actions ? 1 : 0)}>
              {rows.length === 0 ? (emptyText ?? i18n.t('table.empty')) : i18n.t('table.noMatch')}
            </td>
          </tr>
        {/each}
      </tbody>
    </table>
  </div>

  <div class="foot">
    <span class="muted">
      {i18n.t('table.range', { from, to, total: sorted.length })}
      {#if sorted.length !== rows.length}· {i18n.t('table.filteredFrom', { total: rows.length })}{/if}
    </span>
    <span class="grow"></span>
    <label class="size">
      {i18n.t('table.perPage')}
      <select bind:value={size}>
        {#each [25, 50, 100, 250] as n}<option value={n}>{n}</option>{/each}
      </select>
    </label>
    <button disabled={current === 0} onclick={() => (page = 0)} aria-label="«">«</button>
    <button disabled={current === 0} onclick={() => (page = current - 1)} aria-label="‹">‹</button>
    <span class="muted">{current + 1} / {pages}</span>
    <button disabled={current >= pages - 1} onclick={() => (page = current + 1)} aria-label="›">›</button>
    <button disabled={current >= pages - 1} onclick={() => (page = pages - 1)} aria-label="»">»</button>
  </div>
</div>

<style>
  .dt {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
  }
  .bar,
  .foot {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.5rem;
    padding: 0.6rem 0.75rem;
  }
  .bar {
    border-bottom: 1px solid var(--border);
    background: var(--header);
    border-radius: var(--radius) var(--radius) 0 0;
  }
  .foot {
    border-radius: 0 0 var(--radius) var(--radius);
    border-top: 1px solid var(--border);
    font-size: 0.85rem;
  }
  .grow {
    flex: 1;
  }
  .cols {
    position: relative;
  }
  .menu {
    position: absolute;
    right: 0;
    top: calc(100% + 0.25rem);
    z-index: 10;
    min-width: 12rem;
    max-height: 20rem;
    overflow-y: auto;
    padding: 0.4rem;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    box-shadow: 0 6px 20px rgb(0 0 0 / 0.15);
  }
  .menu label {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.3rem 0.4rem;
    cursor: pointer;
    border-radius: var(--radius);
    white-space: nowrap;
  }
  .menu label:hover {
    background: var(--hover);
  }
  .menu input {
    min-height: 0;
    padding: 0;
  }
  .search {
    width: min(22rem, 100%);
  }
  .bar button.on {
    border-color: var(--accent);
    color: var(--accent);
    background: var(--accent-soft);
  }
  .wrap {
    overflow-x: auto;
  }
  .wrap.stale {
    opacity: 0.6;
  }
  table {
    width: 100%;
    border-collapse: collapse;
  }
  th,
  td {
    text-align: left;
    padding: 0.3rem 0.6rem;
    white-space: nowrap;
  }
  th.right,
  td.right {
    text-align: right;
  }
  thead th {
    background: var(--header);
    border-bottom: 1px solid var(--border);
    font-weight: 600;
    font-size: 0.78rem;
    color: var(--muted);
    text-transform: uppercase;
    letter-spacing: 0.03em;
  }
  .sort {
    all: unset;
    cursor: pointer;
    display: inline-flex;
    align-items: center;
    gap: 0.3rem;
    text-transform: inherit;
  }
  .sort:hover {
    color: var(--text);
  }
  .sort:focus-visible {
    outline: 2px solid var(--accent);
  }
  .arrow {
    font-size: 0.6rem;
    color: var(--accent);
    min-width: 0.6rem;
  }
  .filters th {
    padding: 0.35rem 0.5rem;
    text-transform: none;
    font-weight: 400;
  }
  .filters input,
  .filters select {
    width: 100%;
    min-width: 6rem;
    height: 1.8rem;
    padding: 0.2rem 0.5rem;
  }
  tbody td {
    border-top: 1px solid var(--border);
  }
  tbody tr:hover {
    background: var(--hover);
  }
  td.strong {
    font-weight: 600;
  }
  td.muted {
    color: var(--muted);
  }
  td.actions {
    text-align: right;
  }
  td.actions :global(button),
  td.actions :global(a) {
    min-height: 1.5rem;
    padding: 0.1rem 0.45rem;
  }
  .empty {
    text-align: center;
    color: var(--muted);
    padding: 1.5rem;
  }
  .size {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    color: var(--muted);
  }
  .size select {
    height: 1.8rem;
    padding: 0.1rem 0.4rem;
  }
  .foot button {
    min-width: 2rem;
    padding: 0.2rem 0.5rem;
  }
</style>
