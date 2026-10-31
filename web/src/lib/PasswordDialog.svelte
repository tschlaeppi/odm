<script lang="ts">
  import { api, ApiError } from './api'
  import { i18n } from './i18n.svelte'

  let dialog: HTMLDialogElement
  let current = $state('')
  let next = $state('')
  let repeat = $state('')
  let busy = $state(false)
  let error = $state<string | null>(null)
  let done = $state(false)

  export function open() {
    current = next = repeat = ''
    error = null
    done = false
    dialog.showModal()
  }

  async function submit(event: SubmitEvent) {
    event.preventDefault()
    error = null
    if (next !== repeat) {
      error = i18n.t('password.mismatch')
      return
    }
    busy = true
    try {
      await api.changePassword(current, next)
      done = true
    } catch (e) {
      error = e instanceof ApiError && e.detail ? i18n.server(e.detail) : i18n.t('password.failed')
    } finally {
      busy = false
    }
  }
</script>

<dialog bind:this={dialog}>
  <form class="dialog-form" onsubmit={submit}>
    <h2>{i18n.t('password.change')}</h2>
    {#if done}
      <p>{i18n.t('password.changed')}</p>
      <div class="buttons">
        <button type="button" class="primary" onclick={() => dialog.close()}>{i18n.t('common.close')}</button>
      </div>
    {:else}
      <label>
        <span>{i18n.t('password.current')}</span>
        <input type="password" autocomplete="current-password" bind:value={current} required />
      </label>
      <label>
        <span>{i18n.t('password.new')}</span>
        <input type="password" autocomplete="new-password" bind:value={next} required minlength="10" maxlength="256" />
      </label>
      <label>
        <span>{i18n.t('password.repeat')}</span>
        <input type="password" autocomplete="new-password" bind:value={repeat} required minlength="10" maxlength="256" />
      </label>
      <p class="muted">{i18n.t('users.passwordHint')}</p>
      {#if error}<p class="error" role="alert">{error}</p>{/if}
      <div class="buttons">
        <button type="button" class="ghost" onclick={() => dialog.close()}>{i18n.t('common.cancel')}</button>
        <button type="submit" class="primary" disabled={busy}>{i18n.t('common.save')}</button>
      </div>
    {/if}
  </form>
</dialog>

<style>
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
</style>
