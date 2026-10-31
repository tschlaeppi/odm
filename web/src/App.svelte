<script lang="ts">
  import DeviceDetail from './lib/DeviceDetail.svelte'
  import Devices from './lib/Devices.svelte'
  import Dashboard from './lib/Dashboard.svelte'
  import Enrollment from './lib/Enrollment.svelte'
  import GroupEditor from './lib/GroupEditor.svelte'
  import Groups from './lib/Groups.svelte'
  import Login from './lib/Login.svelte'
  import OsUpdates from './lib/OsUpdates.svelte'
  import PackageDetail from './lib/PackageDetail.svelte'
  import ProfileDetail from './lib/ProfileDetail.svelte'
  import Profiles from './lib/Profiles.svelte'
  import Software from './lib/Software.svelte'
  import { auth } from './lib/auth.svelte'
  import { i18n } from './lib/i18n.svelte'
  import { router } from './lib/router.svelte'
  import { theme } from './lib/theme.svelte'

  let collapsed = $state(false)
  try {
    collapsed = localStorage.getItem('odm.sidebar') === 'collapsed'
  } catch {
    // Storage not available
  }

  function toggleSidebar() {
    collapsed = !collapsed
    try {
      localStorage.setItem('odm.sidebar', collapsed ? 'collapsed' : 'expanded')
    } catch {
      // ignorieren
    }
  }

  const deviceId = $derived(router.path.startsWith('/devices/') ? router.path.slice('/devices/'.length) : null)
  const editorId = $derived(router.path.startsWith('/groups/') ? router.path.slice('/groups/'.length) : null)
  const packageId = $derived(router.path.startsWith('/software/') ? router.path.slice('/software/'.length) : null)
  const profileId = $derived(router.path.startsWith('/profiles/') ? router.path.slice('/profiles/'.length) : null)
  const section = $derived(
    router.path.startsWith('/dashboard')
      ? 'dashboard'
      : router.path.startsWith('/groups')
      ? 'groups'
      : router.path.startsWith('/software')
        ? 'software'
        : router.path.startsWith('/profiles')
          ? 'profiles'
          : router.path.startsWith('/os-updates')
            ? 'os-updates'
            : router.path.startsWith('/enrollment')
              ? 'enrollment'
              : 'devices',
  )
</script>

<div class="shell" class:authed={auth.isAuthenticated} class:collapsed>
  <header class="topbar">
    <span class="brand"><span class="logo" aria-hidden="true">◧</span>{i18n.t('app.title')}</span>
    <span class="spacer"></span>
    <button class="ghost" onclick={() => i18n.toggle()} aria-label={i18n.t('lang.toggle')}>
      {i18n.locale === 'de' ? 'EN' : 'DE'}
    </button>
    <button class="ghost" onclick={() => theme.toggle()} aria-label={i18n.t('theme.toggle')}>
      {theme.current === 'dark' ? '☀' : '☾'}
    </button>
    {#if auth.isAuthenticated}
      <span class="user">{auth.user?.displayName}</span>
      <button class="ghost" onclick={() => auth.logout()}>{i18n.t('logout')}</button>
    {/if}
  </header>

  {#if auth.isAuthenticated}
    <nav class="side" aria-label="Navigation">
      <a href="#/dashboard" title={collapsed ? i18n.t('nav.dashboard') : undefined} class:active={section === 'dashboard'}>
        <svg viewBox="0 0 24 24"><rect x="3" y="3" width="8" height="9" rx="1" /><rect x="13" y="3" width="8" height="5" rx="1" /><rect x="13" y="10" width="8" height="11" rx="1" /><rect x="3" y="14" width="8" height="7" rx="1" /></svg>
        <span class="label">{i18n.t('nav.dashboard')}</span>
      </a>
      <div class="group">{i18n.t('nav.group.manage')}</div>
      <a href="#/devices" title={collapsed ? i18n.t('nav.devices') : undefined} class:active={section === 'devices'}>
        <svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="12" rx="1.5" /><path d="M8 20h8M12 16v4" /></svg>
        <span class="label">{i18n.t('nav.devices')}</span>
      </a>
      <a href="#/groups" title={collapsed ? i18n.t('nav.groups') : undefined} class:active={section === 'groups'}>
        <svg viewBox="0 0 24 24"><circle cx="9" cy="8" r="3" /><path d="M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6" /><circle cx="17" cy="9" r="2.3" /><path d="M16 14.2c2.7.2 5 2.4 5 5.8" /></svg>
        <span class="label">{i18n.t('nav.groups')}</span>
      </a>
      <div class="group">{i18n.t('nav.group.deploy')}</div>
      <a href="#/software" title={collapsed ? i18n.t('nav.software') : undefined} class:active={section === 'software'}>
        <svg viewBox="0 0 24 24"><path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z" /><path d="M4 7.5l8 4.5 8-4.5M12 12v9" /></svg>
        <span class="label">{i18n.t('nav.software')}</span>
      </a>
      <a href="#/profiles" title={collapsed ? i18n.t('nav.profiles') : undefined} class:active={section === 'profiles'}>
        <svg viewBox="0 0 24 24"><rect x="5" y="4" width="14" height="17" rx="1.5" /><path d="M9 4V3h6v1M9 10h6M9 14h6M9 18h4" /></svg>
        <span class="label">{i18n.t('nav.profiles')}</span>
      </a>
      <a href="#/os-updates" title={collapsed ? i18n.t('nav.osUpdates') : undefined} class:active={section === 'os-updates'}>
        <svg viewBox="0 0 24 24"><path d="M20 12a8 8 0 1 1-2.3-5.7" /><path d="M20 4v5h-5" /></svg>
        <span class="label">{i18n.t('nav.osUpdates')}</span>
      </a>
      {#if auth.canEdit}
        <div class="group">{i18n.t('nav.group.system')}</div>
        <a href="#/enrollment" title={collapsed ? i18n.t('nav.enrollment') : undefined} class:active={section === 'enrollment'}>
          <svg viewBox="0 0 24 24"><circle cx="8" cy="15" r="4" /><path d="M11 12l9-9M16 7l3 3M14 9l2 2" /></svg>
          <span class="label">{i18n.t('nav.enrollment')}</span>
        </a>
      {/if}
      <span class="fill"></span>
      <button
        class="collapse"
        onclick={toggleSidebar}
        aria-label={collapsed ? i18n.t('nav.expand') : i18n.t('nav.collapse')}
        title={collapsed ? i18n.t('nav.expand') : i18n.t('nav.collapse')}
      >
        <svg viewBox="0 0 24 24">
          {#if collapsed}<path d="M9 6l6 6-6 6" />{:else}<path d="M15 6l-6 6 6 6" />{/if}
        </svg>
        <span class="label">{i18n.t('nav.collapse')}</span>
      </button>
    </nav>
  {/if}

  {#if auth.isAuthenticated}
      <main class="content">
      {#if editorId === 'new'}
        <GroupEditor />
      {:else if editorId}
        {#key editorId}
          <GroupEditor id={editorId} />
        {/key}
      {:else if section === 'dashboard'}
        <Dashboard />
      {:else if section === 'groups'}
        <Groups />
      {:else if section === 'software' && packageId}
        {#key packageId}
          <PackageDetail id={packageId} />
        {/key}
      {:else if section === 'software'}
        <Software />
      {:else if section === 'profiles' && profileId}
        {#key profileId}
          <ProfileDetail id={profileId} />
        {/key}
      {:else if section === 'profiles'}
        <Profiles />
      {:else if section === 'os-updates'}
        <OsUpdates />
      {:else if section === 'enrollment' && auth.canEdit}
        <Enrollment />
      {:else if deviceId}
        {#key deviceId}
          <DeviceDetail id={deviceId} />
        {/key}
      {:else}
        <Devices />
      {/if}
    </main>
  {:else}
    <Login />
  {/if}
</div>

<style>
  .shell {
    min-height: 100vh;
    display: grid;
    grid-template-rows: 3rem 1fr;
    grid-template-columns: 1fr;
  }
  .shell.authed {
    grid-template-columns: 13.5rem minmax(0, 1fr);
  }
  .shell.authed.collapsed {
    grid-template-columns: 3.4rem minmax(0, 1fr);
  }
  .topbar {
    grid-column: 1 / -1;
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0 1rem;
    background: var(--surface);
    border-bottom: 1px solid var(--border);
    border-top: 3px solid var(--accent);
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-weight: 600;
  }
  .logo {
    color: var(--accent);
    font-size: 1.2rem;
  }
  .spacer {
    flex: 1;
  }
  .user {
    margin: 0 0.25rem;
    color: var(--muted);
  }
  .side {
    position: sticky;
    top: 0;
    align-self: start;
    height: calc(100vh - 3rem);
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    padding: 0.5rem 0;
    background: var(--surface);
    border-right: 1px solid var(--border);
  }
  .group {
    padding: 0.9rem 1rem 0.3rem;
    font-size: 0.72rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--muted);
  }
  .side a {
    display: flex;
    align-items: center;
    gap: 0.65rem;
    padding: 0.5rem 1rem;
    color: var(--text);
    text-decoration: none;
    border-left: 3px solid transparent;
  }
  .side a:hover {
    background: var(--hover);
  }
  .side a.active {
    background: var(--accent-soft);
    border-left-color: var(--accent);
    color: var(--accent);
    font-weight: 600;
  }
  .fill {
    flex: 1;
  }
  .collapse {
    display: flex;
    align-items: center;
    gap: 0.65rem;
    margin: 0.5rem;
    padding: 0.4rem 0.5rem;
    color: var(--muted);
    background: transparent;
    border-color: transparent;
  }
  .collapsed .group {
    height: 0;
    padding: 0;
    margin: 0.5rem 0.75rem;
    border-top: 1px solid var(--border);
    overflow: hidden;
    font-size: 0;
  }
  .collapsed .label {
    display: none;
  }
  .collapsed .side a {
    justify-content: center;
    padding: 0.55rem 0;
  }
  .collapsed .collapse {
    justify-content: center;
    margin: 0.5rem 0.4rem;
  }
  svg {
    width: 1.15rem;
    height: 1.15rem;
    fill: none;
    stroke: currentColor;
    stroke-width: 1.7;
    stroke-linecap: round;
    stroke-linejoin: round;
    flex: none;
  }
  .content {
    min-width: 0;
    padding: 1.25rem 1.5rem;
  }

  @media (max-width: 760px) {
    .shell.authed,
    .shell.authed.collapsed {
      grid-template-columns: minmax(0, 1fr);
      grid-template-rows: 3rem auto 1fr;
    }
    .side {
      position: static;
      height: auto;
      flex-direction: row;
      overflow-x: auto;
      padding: 0;
      border-right: 0;
      border-bottom: 1px solid var(--border);
    }
    .group,
    .fill,
    .collapse {
      display: none;
    }
    .collapsed .label {
      display: inline;
    }
    .side a {
      border-left: 0;
      border-bottom: 3px solid transparent;
      white-space: nowrap;
    }
    .side a.active {
      border-bottom-color: var(--accent);
    }
  }
</style>
