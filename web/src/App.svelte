<script lang="ts">
  import Devices from './lib/Devices.svelte'
  import Enrollment from './lib/Enrollment.svelte'
  import GroupEditor from './lib/GroupEditor.svelte'
  import Groups from './lib/Groups.svelte'
  import Login from './lib/Login.svelte'
  import OsUpdates from './lib/OsUpdates.svelte'
  import PackageDetail from './lib/PackageDetail.svelte'
  import Software from './lib/Software.svelte'
  import { auth } from './lib/auth.svelte'
  import { i18n } from './lib/i18n.svelte'
  import { router } from './lib/router.svelte'
  import { theme } from './lib/theme.svelte'

  const editorId = $derived(router.path.startsWith('/groups/') ? router.path.slice('/groups/'.length) : null)
  const packageId = $derived(router.path.startsWith('/software/') ? router.path.slice('/software/'.length) : null)
  const section = $derived(
    router.path.startsWith('/groups')
      ? 'groups'
      : router.path.startsWith('/software')
        ? 'software'
        : router.path.startsWith('/os-updates')
          ? 'os-updates'
          : router.path.startsWith('/enrollment')
            ? 'enrollment'
            : 'devices',
  )
</script>

<header class="topbar">
  <span class="brand">{i18n.t('app.title')}</span>
  {#if auth.isAuthenticated}
    <nav>
      <a href="#/devices" class:active={section === 'devices'}>{i18n.t('nav.devices')}</a>
      <a href="#/groups" class:active={section === 'groups'}>{i18n.t('nav.groups')}</a>
      <a href="#/software" class:active={section === 'software'}>{i18n.t('nav.software')}</a>
      <a href="#/os-updates" class:active={section === 'os-updates'}>{i18n.t('nav.osUpdates')}</a>
      {#if auth.canEdit}
        <a href="#/enrollment" class:active={section === 'enrollment'}>{i18n.t('nav.enrollment')}</a>
      {/if}
    </nav>
  {/if}
  <span class="spacer"></span>
  <button class="ghost" onclick={() => i18n.toggle()} aria-label={i18n.t('lang.toggle')}>
    {i18n.locale === 'de' ? 'EN' : 'DE'}
  </button>
  <button class="ghost" onclick={() => theme.toggle()} aria-label={i18n.t('theme.toggle')}>
    {theme.current === 'dark' ? '☀' : '☾'}
  </button>
  {#if auth.isAuthenticated}
    <span class="muted user">{auth.user?.displayName}</span>
    <button class="ghost" onclick={() => auth.logout()}>{i18n.t('logout')}</button>
  {/if}
</header>

{#if auth.isAuthenticated}
  <main class="content">
    {#if editorId === 'new'}
      <GroupEditor />
    {:else if editorId}
      {#key editorId}
        <GroupEditor id={editorId} />
      {/key}
    {:else if section === 'groups'}
      <Groups />
    {:else if section === 'software' && packageId}
      {#key packageId}
        <PackageDetail id={packageId} />
      {/key}
    {:else if section === 'software'}
      <Software />
    {:else if section === 'os-updates'}
      <OsUpdates />
    {:else if section === 'enrollment' && auth.canEdit}
      <Enrollment />
    {:else}
      <Devices />
    {/if}
  </main>
{:else}
  <Login />
{/if}

<style>
  .topbar {
    height: 3.5rem;
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0 1.25rem;
    border-bottom: 1px solid var(--border);
    background: var(--surface);
  }
  .brand {
    font-weight: 700;
    letter-spacing: -0.01em;
    margin-right: 1rem;
  }
  nav {
    display: flex;
    gap: 0.25rem;
  }
  nav a {
    color: var(--muted);
    text-decoration: none;
    padding: 0.4rem 0.7rem;
    border-radius: 0.45rem;
  }
  nav a:hover {
    background: var(--hover);
  }
  nav a.active {
    color: var(--text);
    font-weight: 600;
  }
  .spacer {
    flex: 1;
  }
  .user {
    margin: 0 0.25rem;
    font-size: 0.9rem;
  }
  .content {
    max-width: 72rem;
    margin: 0 auto;
    padding: 1.5rem 1.25rem;
  }
</style>
