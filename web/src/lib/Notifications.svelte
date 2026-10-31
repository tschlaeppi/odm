<script lang="ts">
  import { onMount } from 'svelte'
  import { api, ApiError, type ChannelResultDto, type NotificationsDto, type SentAlertDto } from './api'
  import DataTable, { type Column } from './DataTable.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'

  let data = $state<NotificationsDto | null>(null)
  let failed = $state(false)
  let testing = $state(false)
  let results = $state<ChannelResultDto[]>([])
  let testError = $state('')

  async function load() {
    try {
      data = await api.notifications()
      failed = false
    } catch {
      failed = true
    }
  }

  onMount(load)

  async function test() {
    testing = true
    testError = ''
    results = []
    try {
      results = await api.notificationsTest()
      await load()
    } catch (e) {
      testError = e instanceof ApiError && e.status === 400 ? i18n.t('ntf.noChannel') : i18n.t('devices.error')
    } finally {
      testing = false
    }
  }

  function typeLabel(type: string) {
    const key = `ntf.type.${type}` as MessageKey
    const label = i18n.t(key)
    return label === key ? type : label
  }

  const columns = $derived<Column<SentAlertDto>[]>([
    { key: 'time', label: i18n.t('audit.time'), value: (a) => a.sentAt, cell: timeCell, filter: 'none' },
    { key: 'type', label: i18n.t('ntf.type'), value: (a) => typeLabel(a.type), filter: 'select' },
    { key: 'device', label: i18n.t('inv.device'), value: (a) => a.deviceName, strong: true },
    { key: 'subject', label: i18n.t('ntf.subject'), value: (a) => a.subject },
    { key: 'detail', label: i18n.t('ntf.detail'), value: (a) => a.detail, muted: true },
    { key: 'channels', label: i18n.t('ntf.channels'), value: (a) => a.channels, filter: 'select' },
  ])
</script>

{#snippet timeCell(a: SentAlertDto)}
  {i18n.formatDate(a.sentAt)}
{/snippet}

<section>
  <h1>{i18n.t('ntf.title')}</h1>
  <p class="muted intro">{i18n.t('ntf.intro')}</p>

  {#if failed}
    <p class="error">{i18n.t('devices.error')}</p>
  {:else if data}
    <dl class="card facts">
      <div>
        <dt>{i18n.t('ntf.status')}</dt>
        <dd><span class="badge" class:ok={data.enabled}>{data.enabled ? i18n.t('ntf.on') : i18n.t('ntf.off')}</span></dd>
      </div>
      {#each data.channels as c}
        <div>
          <dt>{c.name}</dt>
          <dd><span class="badge" class:ok={c.configured}>{c.configured ? i18n.t('ntf.configured') : i18n.t('ntf.notConfigured')}</span></dd>
        </div>
      {/each}
      <div><dt>{i18n.t('ntf.events')}</dt><dd>{data.events.map(typeLabel).join(', ')}</dd></div>
    </dl>

    <div class="test">
      <button class="ghost" disabled={testing} onclick={test}>{i18n.t('ntf.test')}</button>
      {#each results as r}
        <span class="badge" class:ok={r.success} class:err={!r.success} title={r.error ?? undefined}>{r.channel}: {r.success ? i18n.t('ntf.sent') : i18n.t('ntf.failed')}</span>
      {/each}
      {#if testError}<span class="error">{testError}</span>{/if}
    </div>

    <h2>{i18n.t('ntf.history')}</h2>
    <DataTable rows={data.recent} {columns} rowKey={(a) => a.id} emptyText={i18n.t('ntf.empty')} pageSize={25} />
  {/if}
</section>

<style>
  h1 {
    font-size: 1.35rem;
    margin: 0 0 0.5rem;
  }
  h2 {
    font-size: 1rem;
    margin: 1.5rem 0 0.5rem;
  }
  .intro {
    margin: 0 0 1rem;
    max-width: 46rem;
  }
  .test {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    align-items: center;
    margin-top: 1rem;
  }
</style>
