<script lang="ts">
  import { ApiError } from './api'
  import { auth } from './auth.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  let userName = $state('')
  let password = $state('')
  let busy = $state(false)
  let errorKey = $state<MessageKey | null>(null)

  async function submit(event: SubmitEvent) {
    event.preventDefault()
    busy = true
    errorKey = null
    try {
      await auth.login(userName, password)
    } catch (e) {
      if (e instanceof ApiError) {
        errorKey = e.status === 429 ? 'login.tooMany' : 'login.failed'
      } else {
        errorKey = 'login.error'
      }
      password = ''
    } finally {
      busy = false
    }
  }
</script>

<main class="login">
  <form class="card" onsubmit={submit}>
    <h1>{i18n.t('app.title')}</h1>
    <h2>{i18n.t('login.title')}</h2>

    <label>
      <span>{i18n.t('login.userName')}</span>
      <input bind:value={userName} autocomplete="username" required />
    </label>

    <label>
      <span>{i18n.t('login.password')}</span>
      <input type="password" bind:value={password} autocomplete="current-password" required />
    </label>

    {#if errorKey}
      <p class="error" role="alert">{i18n.t(errorKey)}</p>
    {/if}

    <button class="primary" type="submit" disabled={busy}>{i18n.t('login.submit')}</button>
  </form>
</main>

<style>
  .login {
    min-height: calc(100vh - 3.5rem);
    display: grid;
    place-items: center;
    padding: 1rem;
  }
  form {
    width: min(22rem, 100%);
    display: grid;
    gap: 1rem;
  }
  h1 {
    font-size: 1.25rem;
    margin: 0;
  }
  h2 {
    font-size: 0.95rem;
    font-weight: 500;
    margin: -0.5rem 0 0;
    color: var(--muted);
  }
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  .error {
    margin: 0;
    color: var(--danger);
    font-size: 0.9rem;
  }
</style>
