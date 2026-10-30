<script lang="ts">
  import { onMount } from 'svelte'
  import { api, type DeviceDto, type GroupDto } from './api'
  import { i18n, type MessageKey } from './i18n.svelte'

  let {
    onpick,
    label,
  }: {
    onpick: (kind: 'Device' | 'Group', target: string) => void
    label: string
  } = $props()

  let kind = $state<'Device' | 'Group'>('Group')
  let groups = $state<GroupDto[]>([])
  let groupId = $state('')
  let search = $state('')
  let results = $state<DeviceDto[]>([])
  let deviceId = $state('')

  onMount(async () => {
    try {
      groups = await api.groups()
    } catch {
      groups = []
    }
  })

  $effect(() => {
    const term = search.trim()
    if (kind !== 'Device' || term.length < 2) {
      results = []
      return
    }
    const handle = setTimeout(async () => {
      try {
        results = (await api.devices({ search: term, take: 20 })).items
      } catch {
        results = []
      }
    }, 250)
    return () => clearTimeout(handle)
  })

  function submit(event: SubmitEvent) {
    event.preventDefault()
    const target = kind === 'Group' ? groupId : deviceId
    if (!target) return
    onpick(kind, target)
    groupId = ''
    deviceId = ''
    search = ''
  }
</script>

<form class="picker" onsubmit={submit}>
  <select bind:value={kind} aria-label={label}>
    {#each ['Group', 'Device'] as k}
      <option value={k}>{i18n.t(`target.kind.${k}` as MessageKey)}</option>
    {/each}
  </select>

  {#if kind === 'Group'}
    <select bind:value={groupId} required aria-label={i18n.t('target.pickGroup')}>
      <option value="" disabled>{i18n.t('target.pickGroup')}</option>
      {#each groups as g (g.id)}
        <option value={g.id}>{g.name} ({g.deviceCount})</option>
      {/each}
    </select>
  {:else}
    <input bind:value={search} placeholder={i18n.t('target.searchDevice')} />
    <select bind:value={deviceId} required aria-label={i18n.t('target.pickDevice')}>
      <option value="" disabled>{i18n.t('target.pickDevice')}</option>
      {#each results as d (d.id)}
        <option value={d.id}>{d.name}</option>
      {/each}
    </select>
  {/if}

  <button type="submit">{label}</button>
</form>

<style>
  .picker {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    align-items: center;
  }
  .picker input {
    min-width: 10rem;
  }
</style>
