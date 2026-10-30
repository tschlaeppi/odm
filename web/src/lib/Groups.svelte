<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type GroupDto } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n } from './i18n.svelte'
  import { router } from './router.svelte'

  let groups = $state<GroupDto[]>([])
  let loading = $state(true)
  let failed = $state(false)

  async function load() {
    loading = true
    failed = false
    try {
      groups = await api.groups()
    } catch {
      failed = true
    } finally {
      loading = false
    }
  }

  async function remove(group: GroupDto) {
    if (!confirm(i18n.t('groups.confirmDelete', { name: group.name }))) return
    try {
      await api.deleteGroup(group.id)
      groups = groups.filter((g) => g.id !== group.id)
    } catch {
      alert(i18n.t('common.saveFailed'))
    }
  }

  onMount(load)

  const columns = $derived<Column<GroupDto>[]>([
    { key: 'name', label: i18n.t('groups.name'), value: (g) => g.name, cell: nameCell, strong: true },
    { key: 'desc', label: i18n.t('groups.description'), value: (g) => g.description, muted: true },
    { key: 'devices', label: i18n.t('groups.devicesLabel'), value: (g) => g.deviceCount, align: 'right', filter: 'none' },
    { key: 'rules', label: i18n.t('groups.rulesLabel'), value: (g) => g.rules.length, align: 'right', filter: 'none' },
  ])
</script>

{#snippet nameCell(group: GroupDto)}
  <a href={`#/groups/${group.id}`}>{group.name}</a>
{/snippet}

{#snippet rowActions(group: GroupDto)}
  {#if auth.canEdit}
    <a class="link" href={`#/groups/${group.id}`}>{i18n.t('common.edit')}</a>
    <button class="ghost" onclick={() => remove(group)}>{i18n.t('common.delete')}</button>
  {/if}
{/snippet}

<section>
  <header class="page-head">
    <h1>{i18n.t('groups.title')}</h1>
    <span class="spacer"></span>
    {#if auth.canEdit}
      <button class="primary" onclick={() => router.navigate('/groups/new')}>{i18n.t('groups.new')}</button>
    {/if}
  </header>

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if failed}
    <div class="card">
      <p class="error">{i18n.t('groups.error')}</p>
      <button onclick={load}>{i18n.t('devices.retry')}</button>
    </div>
  {:else}
    <DataTable rows={groups} {columns} rowKey={(g) => g.id} actions={rowActions} emptyText={i18n.t('groups.empty')} />
  {/if}
</section>

<style>
  .link {
    text-decoration: none;
    padding: 0.25rem 0.55rem;
  }
  .error {
    color: var(--danger);
    margin-top: 0;
  }
</style>
