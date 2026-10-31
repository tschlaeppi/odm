<script lang="ts">
  import { api, ApiError, type DeviceDto, type JobDto } from './api'
  import { i18n, type MessageKey } from './i18n.svelte'

  let dialog: HTMLDialogElement
  let device = $state<DeviceDto | null>(null)
  let jobs = $state<JobDto[]>([])
  let busy = $state(false)
  let info = $state<string | null>(null)
  let errorKey = $state<MessageKey | null>(null)
  let timer: ReturnType<typeof setInterval> | undefined

  async function refresh() {
    if (!device) return
    try {
      jobs = await api.jobs(device.id)
    } catch {
      /* next attempt at the next interval */
    }
  }

  export function open(target: DeviceDto) {
    device = target
    jobs = []
    errorKey = null
    info = null
    dialog.showModal()
    refresh()
    // Results only arrive with the next heartbeat: refresh regularly while any are open.
    timer = setInterval(refresh, 5000)
  }

  function closed() {
    clearInterval(timer)
    device = null
  }

  async function runUpdates() {
    if (!device) return
    busy = true
    errorKey = null
    info = null
    try {
      await api.runOsUpdatesNow(device.id)
      info = i18n.t('os.runStarted')
    } catch (e) {
      errorKey =
        e instanceof ApiError && e.status === 400
          ? 'os.noPolicyForDevice'
          : e instanceof ApiError && e.status === 409
            ? 'jobs.noAgent'
            : 'common.saveFailed'
    } finally {
      busy = false
    }
  }

  async function ping() {
    if (!device) return
    busy = true
    errorKey = null
    try {
      await api.createJob(device.id, 'ping')
      await refresh()
    } catch (e) {
      errorKey = e instanceof ApiError && e.status === 409 ? 'jobs.noAgent' : 'common.saveFailed'
    } finally {
      busy = false
    }
  }
</script>

<dialog bind:this={dialog} onclose={closed}>
  <div class="dialog-form wide">
    <h2>{i18n.t('jobs.title')}</h2>
    <p class="muted">{device?.name}</p>

    {#if errorKey}
      <p class="error" role="alert">{i18n.t(errorKey)}</p>
    {/if}
    {#if info}
      <p class="muted">{info}</p>
    {/if}

    {#if jobs.length === 0}
      <p class="muted">{i18n.t('jobs.empty')}</p>
    {:else}
      <div class="scroll">
        <table>
          <thead>
            <tr>
              <th>{i18n.t('jobs.type')}</th>
              <th>{i18n.t('devices.status')}</th>
              <th>{i18n.t('jobs.created')}</th>
              <th>{i18n.t('jobs.result')}</th>
            </tr>
          </thead>
          <tbody>
            {#each jobs as job (job.id)}
              <tr>
                <td>{job.type}</td>
                <td>{i18n.t(`jobs.status.${job.status}` as MessageKey)}</td>
                <td>{i18n.formatDate(job.createdAt)}</td>
                <td class="output">{i18n.server(job.output) || '–'}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}

    <div class="buttons">
      <button type="button" onclick={() => dialog.close()}>{i18n.t('common.close')}</button>
      <button type="button" onclick={runUpdates} disabled={busy}>{i18n.t('os.runNow')}</button>
      <button class="primary" type="button" onclick={ping} disabled={busy}>{i18n.t('jobs.ping')}</button>
    </div>
  </div>
</dialog>

<style>
  dialog {
    width: min(42rem, calc(100vw - 2rem));
  }
  .scroll {
    max-height: 18rem;
    overflow: auto;
  }
  table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.88rem;
  }
  th,
  td {
    text-align: left;
    padding: 0.45rem 0.6rem;
    vertical-align: top;
  }
  th {
    font-weight: 500;
    font-size: 0.78rem;
    color: var(--muted);
    border-bottom: 1px solid var(--border);
  }
  tbody tr + tr td {
    border-top: 1px solid var(--border);
  }
  .output {
    word-break: break-word;
    color: var(--muted);
  }
  .error {
    color: var(--danger);
    margin: 0;
  }
</style>
