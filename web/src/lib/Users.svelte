<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type ManagedUserDto } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  const roles = ['Admin', 'Operator', 'ReadOnly'] as const

  let users = $state<ManagedUserDto[]>([])
  let loading = $state(true)
  let failed = $state(false)
  let error = $state<string | null>(null)

  let userName = $state('')
  let displayName = $state('')
  let password = $state('')
  let role = $state<string>('Operator')
  let busy = $state(false)

  let editDialog: HTMLDialogElement
  let pwDialog: HTMLDialogElement
  let editing = $state<ManagedUserDto | null>(null)
  let editName = $state('')
  let editRole = $state('Operator')
  let editActive = $state(true)
  let newPassword = $state('')
  let dialogError = $state<string | null>(null)

  const isSelf = (u: ManagedUserDto) => u.id === auth.user?.id

  function message(e: unknown, fallback: MessageKey) {
    return e instanceof ApiError && e.detail ? i18n.server(e.detail) : i18n.t(fallback)
  }

  async function load() {
    try {
      users = await api.users()
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
    error = null
    try {
      await api.createUser(userName.trim(), displayName.trim(), password, role)
      userName = displayName = password = ''
      await load()
    } catch (e) {
      error = message(e, 'common.saveFailed')
    } finally {
      busy = false
    }
  }

  function edit(u: ManagedUserDto) {
    editing = u
    editName = u.displayName
    editRole = u.role
    editActive = u.isActive
    dialogError = null
    editDialog.showModal()
  }

  async function saveEdit(event: SubmitEvent) {
    event.preventDefault()
    if (!editing) return
    try {
      await api.updateUser(editing.id, editName.trim(), editRole, editActive)
      editDialog.close()
      await load()
    } catch (e) {
      dialogError = message(e, 'common.saveFailed')
    }
  }

  function resetPassword(u: ManagedUserDto) {
    editing = u
    newPassword = ''
    dialogError = null
    pwDialog.showModal()
  }

  async function savePassword(event: SubmitEvent) {
    event.preventDefault()
    if (!editing) return
    try {
      await api.setUserPassword(editing.id, newPassword)
      pwDialog.close()
    } catch (e) {
      dialogError = message(e, 'common.saveFailed')
    }
  }

  async function remove(u: ManagedUserDto) {
    if (!confirm(i18n.t('users.deleteConfirm'))) return
    error = null
    try {
      await api.deleteUser(u.id)
      await load()
    } catch (e) {
      error = message(e, 'common.saveFailed')
    }
  }

  const columns = $derived<Column<ManagedUserDto>[]>([
    { key: 'userName', label: i18n.t('users.userName'), value: (u) => u.userName, cell: nameCell, strong: true },
    { key: 'displayName', label: i18n.t('users.displayName'), value: (u) => u.displayName },
    { key: 'role', label: i18n.t('users.role'), value: (u) => i18n.t(`users.role.${u.role}` as MessageKey), filter: 'select' },
    { key: 'status', label: i18n.t('users.status'), value: (u) => (u.isActive ? i18n.t('users.active') : i18n.t('users.inactive')), filter: 'select' },
    { key: 'created', label: i18n.t('users.created'), value: (u) => u.createdAt, cell: createdCell, filter: 'none' },
  ])
</script>

{#snippet nameCell(u: ManagedUserDto)}
  {u.userName} {#if isSelf(u)}<span class="muted">{i18n.t('users.you')}</span>{/if}
{/snippet}

{#snippet createdCell(u: ManagedUserDto)}
  {i18n.formatDate(u.createdAt)}
{/snippet}

{#snippet rowActions(u: ManagedUserDto)}
  <button class="ghost" onclick={() => edit(u)}>{i18n.t('common.edit')}</button>
  <button class="ghost" onclick={() => resetPassword(u)}>{i18n.t('users.resetPassword')}</button>
  {#if !isSelf(u)}
    <button class="ghost" onclick={() => remove(u)}>{i18n.t('common.delete')}</button>
  {/if}
{/snippet}

<section>
  <h1>{i18n.t('users.title')}</h1>
  <p class="muted intro">{i18n.t('users.intro')}</p>

  <form class="card form" onsubmit={create}>
    <div class="row">
      <label>
        <span>{i18n.t('users.userName')}</span>
        <input bind:value={userName} required maxlength="128" pattern="[A-Za-z0-9._@\-]+" autocomplete="off" />
      </label>
      <label>
        <span>{i18n.t('users.displayName')}</span>
        <input bind:value={displayName} maxlength="256" autocomplete="off" />
      </label>
      <label>
        <span>{i18n.t('users.password')}</span>
        <input type="password" bind:value={password} required minlength="10" maxlength="256" autocomplete="new-password" />
      </label>
      <label>
        <span>{i18n.t('users.role')}</span>
        <select bind:value={role}>
          {#each roles as r}<option value={r}>{i18n.t(`users.role.${r}` as MessageKey)}</option>{/each}
        </select>
      </label>
      <button class="primary" type="submit" disabled={busy}>{i18n.t('users.create')}</button>
    </div>
    <p class="muted small">{i18n.t('users.passwordHint')}</p>
    {#if error}<p class="error" role="alert">{error}</p>{/if}
  </form>

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if failed}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else}
    <DataTable rows={users} {columns} rowKey={(u) => u.id} actions={rowActions} emptyText={i18n.t('users.empty')} />
  {/if}
</section>

<dialog bind:this={editDialog}>
  <form class="dialog-form" onsubmit={saveEdit}>
    <h2>{i18n.t('users.editTitle')}: {editing?.userName}</h2>
    <label>
      <span>{i18n.t('users.displayName')}</span>
      <input bind:value={editName} maxlength="256" />
    </label>
    <label>
      <span>{i18n.t('users.role')}</span>
      <select bind:value={editRole} disabled={editing ? isSelf(editing) : false}>
        {#each roles as r}<option value={r}>{i18n.t(`users.role.${r}` as MessageKey)}</option>{/each}
      </select>
    </label>
    <label class="check">
      <input type="checkbox" bind:checked={editActive} disabled={editing ? isSelf(editing) : false} />
      <span>{i18n.t('users.active')}</span>
    </label>
    {#if dialogError}<p class="error" role="alert">{dialogError}</p>{/if}
    <div class="buttons">
      <button type="button" class="ghost" onclick={() => editDialog.close()}>{i18n.t('common.cancel')}</button>
      <button type="submit" class="primary">{i18n.t('common.save')}</button>
    </div>
  </form>
</dialog>

<dialog bind:this={pwDialog}>
  <form class="dialog-form" onsubmit={savePassword}>
    <h2>{i18n.t('users.resetTitle')} {editing?.userName}</h2>
    <label>
      <span>{i18n.t('password.new')}</span>
      <input type="password" bind:value={newPassword} required minlength="10" maxlength="256" autocomplete="new-password" />
    </label>
    <p class="muted">{i18n.t('users.passwordHint')}</p>
    {#if dialogError}<p class="error" role="alert">{dialogError}</p>{/if}
    <div class="buttons">
      <button type="button" class="ghost" onclick={() => pwDialog.close()}>{i18n.t('common.cancel')}</button>
      <button type="submit" class="primary">{i18n.t('common.save')}</button>
    </div>
  </form>
</dialog>

<style>
  h1 {
    font-size: 1.35rem;
    margin: 0 0 0.5rem;
  }
  .intro {
    margin: 0 0 1rem;
    max-width: 46rem;
  }
  .form {
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
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  label.check {
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }
  .small {
    font-size: 0.85rem;
    margin: 0;
  }
</style>
