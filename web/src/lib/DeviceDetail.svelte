<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type DeviceDto, type DeviceGroupRefDto, type InstallationDto } from './api'
  import { auth } from './auth.svelte'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'
  import MessageDialog from './MessageDialog.svelte'
  import { isOnline } from './status'

  let { id }: { id: string } = $props()

  let device = $state<DeviceDto | null>(null)
  let groups = $state<DeviceGroupRefDto[]>([])
  let installations = $state<InstallationDto[]>([])
  let loading = $state(true)
  let notFound = $state(false)
  let failed = $state(false)

  let messageDialog: MessageDialog
  let certMessage = $state('')
  let certBusy = $state(false)

  async function resetCertificate() {
    if (!device || !confirm(i18n.t('dev.detail.certConfirm', { name: device.name }))) return
    certBusy = true
    certMessage = ''
    try {
      await api.resetDeviceCertificate(device.id)
      certMessage = i18n.t('dev.detail.certDone')
    } catch {
      certMessage = i18n.t('devices.error')
    } finally {
      certBusy = false
    }
  }

  const statusClass = { Pending: '', Running: 'run', Installed: 'ok', Failed: 'err', Removed: '' } as const

  async function load() {
    try {
      const [d, g, i] = await Promise.all([api.device(id), api.deviceGroups(id), api.deviceInstallations(id)])
      device = d
      groups = g
      installations = i
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) notFound = true
      else failed = true
    } finally {
      loading = false
    }
  }

  onMount(load)

  async function retry(installation: InstallationDto) {
    await api.retryInstallation(installation.id)
    installations = await api.deviceInstallations(id)
  }

  const groupColumns = $derived<Column<DeviceGroupRefDto>[]>([
    { key: 'name', label: i18n.t('groups.name'), value: (g) => g.name, cell: groupName, strong: true },
    { key: 'desc', label: i18n.t('groups.description'), value: (g) => g.description, muted: true },
  ])

  const softwareColumns = $derived<Column<InstallationDto>[]>([
    { key: 'package', label: i18n.t('sw.name'), value: (i) => i.packageName, cell: packageName, strong: true },
    { key: 'status', label: i18n.t('sw.status'), value: (i) => i18n.t(`sw.status.${i.status}` as MessageKey), cell: status, filter: 'select' },
    { key: 'target', label: i18n.t('dev.detail.target'), value: (i) => i.targetVersion },
    { key: 'installed', label: i18n.t('dev.detail.installed'), value: (i) => i.installedVersion },
    { key: 'attempts', label: i18n.t('dev.detail.attempts'), value: (i) => i.attempts, align: 'right', filter: 'none' },
    { key: 'updated', label: i18n.t('dev.detail.updated'), value: (i) => i.updatedAt, cell: updated, filter: 'none' },
    { key: 'message', label: i18n.t('dev.detail.message'), value: (i) => i.message, cell: message, muted: true },
  ])
</script>

{#snippet groupName(g: DeviceGroupRefDto)}
  <a href={`#/groups/${g.id}`}>{g.name}</a>
{/snippet}

{#snippet packageName(i: InstallationDto)}
  <a href={`#/software/${i.packageId}`}>{i.packageName}</a>
{/snippet}

{#snippet status(i: InstallationDto)}
  <span class="badge {statusClass[i.status]}">{i.removal && (i.status === 'Pending' || i.status === 'Running' || i.status === 'Failed') ? i18n.t(`sw.removal.${i.status}` as MessageKey) : i18n.t(`sw.status.${i.status}` as MessageKey)}</span>
{/snippet}

{#snippet updated(i: InstallationDto)}
  {i18n.formatDate(i.updatedAt)}
{/snippet}

{#snippet message(i: InstallationDto)}
  <span class="msg" title={i.message ?? ''}>{i.message ?? '–'}</span>
{/snippet}

{#snippet softwareActions(i: InstallationDto)}
  {#if i.message}
    <button class="ghost" onclick={() => messageDialog.show(`${i.packageName} – ${i18n.t('msg.title')}`, i.message)}>
      {i18n.t('msg.show')}
    </button>
  {/if}
  {#if auth.canEdit && i.status === 'Failed'}
    <button class="ghost" onclick={() => retry(i)}>{i18n.t('sw.retry')}</button>
  {/if}
{/snippet}

<section>
  <p><a href="#/devices">← {i18n.t('devices.title')}</a></p>

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if notFound}
    <p class="muted">{i18n.t('dev.detail.notFound')}</p>
  {:else if failed || !device}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else}
    <header class="page-head">
      <h1>{device.name}</h1>
      <span class="badge" class:ok={isOnline(device)}>
        {isOnline(device) ? i18n.t('devices.online') : i18n.t('devices.offline')}
      </span>
    </header>

    <dl class="card facts">
      <div><dt>{i18n.t('devices.os')}</dt><dd>{device.osName} {device.osVersion}</dd></div>
      <div><dt>{i18n.t('dev.detail.agent')}</dt><dd>{device.agentVersion ?? '–'}</dd></div>
      <div><dt>{i18n.t('devices.lastSeen')}</dt><dd>{device.lastSeenAt ? i18n.formatDate(device.lastSeenAt) : '–'}</dd></div>
      <div><dt>{i18n.t('dev.detail.registered')}</dt><dd>{i18n.formatDate(device.registeredAt)}</dd></div>
      <div><dt>{i18n.t('devices.primaryUser')}</dt><dd>{device.primaryUser ?? '–'}</dd></div>
      <div><dt>{i18n.t('devices.orgUnit')}</dt><dd>{device.orgUnit ?? '–'}</dd></div>
      <div><dt>{i18n.t('devices.costCenter')}</dt><dd>{device.costCenter ?? '–'}</dd></div>
    </dl>

    {#if auth.isAdmin}
      <div class="cert">
        <button class="ghost" disabled={certBusy} onclick={resetCertificate}>{i18n.t('dev.detail.certReset')}</button>
        <span class="muted">{certMessage || i18n.t('dev.detail.certResetHint')}</span>
      </div>
    {/if}

    <h2>{i18n.t('dev.detail.groups')} ({groups.length})</h2>
    <DataTable rows={groups} columns={groupColumns} rowKey={(g) => g.id} emptyText={i18n.t('dev.detail.groupsEmpty')} pageSize={25} />

    <h2>{i18n.t('dev.detail.software')} ({installations.length})</h2>
    <DataTable
      rows={installations}
      columns={softwareColumns}
      rowKey={(i) => i.id}
      actions={softwareActions}
      emptyText={i18n.t('dev.detail.softwareEmpty')}
      pageSize={25}
    />
  {/if}
</section>

<MessageDialog bind:this={messageDialog} />

<style>
  h2 {
    font-size: 1rem;
    margin: 1.5rem 0 0.5rem;
  }
  .facts {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
    gap: 0.9rem 1.5rem;
    margin: 0;
  }
  .facts div {
    min-width: 0;
  }
  dt {
    font-size: 0.75rem;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    color: var(--muted);
  }
  dd {
    margin: 0.1rem 0 0;
    font-weight: 600;
    overflow-wrap: anywhere;
  }
  .msg {
    display: inline-block;
    max-width: 24rem;
    overflow: hidden;
    text-overflow: ellipsis;
    vertical-align: bottom;
  }
  .cert {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 0.75rem;
    margin-top: 1rem;
  }
  .error {
    color: var(--danger);
  }
</style>
