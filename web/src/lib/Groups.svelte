<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type GroupDto } from './api'
  import { auth } from './auth.svelte'
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
</script>

<section>
  <header class="head">
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
  {:else if groups.length === 0}
    <p class="muted">{i18n.t('groups.empty')}</p>
  {:else}
    <div class="list">
      {#each groups as group (group.id)}
        <article class="card item">
          <div class="info">
            <h2>{group.name}</h2>
            {#if group.description}<p class="muted">{group.description}</p>{/if}
            <p class="meta muted">
              {i18n.t('groups.devices', { count: group.deviceCount })} ·
              {i18n.t('groups.rules', { count: group.rules.length })}
            </p>
          </div>
          <div class="buttons">
            <a class="link" href={`#/groups/${group.id}`}>{auth.canEdit ? i18n.t('common.edit') : i18n.t('groups.title')}</a>
            {#if auth.canEdit}
              <button class="ghost" onclick={() => remove(group)}>{i18n.t('common.delete')}</button>
            {/if}
          </div>
        </article>
      {/each}
    </div>
  {/if}
</section>

<style>
  .head {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1rem;
  }
  .spacer {
    flex: 1;
  }
  h1 {
    font-size: 1.35rem;
    margin: 0;
  }
  h2 {
    font-size: 1rem;
    margin: 0;
  }
  .list {
    display: grid;
    gap: 0.75rem;
  }
  .item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem 1.25rem;
  }
  .info p {
    margin: 0.2rem 0 0;
    font-size: 0.9rem;
  }
  .buttons {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  .link {
    color: var(--accent);
    text-decoration: none;
    padding: 0.4rem 0.6rem;
  }
  .error {
    color: var(--danger);
    margin-top: 0;
  }
</style>
