<script lang="ts">
  import { onMount } from 'svelte'
  import {
    api,
    ApiError,
    type DeviceDto,
    type GroupField,
    type GroupInput,
    type GroupRule,
    type MatchMode,
    type MatchOperator,
  } from './api'
  import { auth } from './auth.svelte'
  import { i18n, type MessageKey } from './i18n.svelte'
  import { router } from './router.svelte'

  let { id = null }: { id?: string | null } = $props()

  const fields: GroupField[] = ['Name', 'OsFamily', 'OsName', 'OsVersion', 'PrimaryUser', 'OrgUnit', 'CostCenter']
  const operators: MatchOperator[] = ['Is', 'StartsWith', 'EndsWith', 'Contains']
  const osFamilies = ['Windows', 'Linux']

  let name = $state('')
  let description = $state('')
  let matchMode = $state<MatchMode>('All')
  let rules = $state<GroupRule[]>([{ field: 'Name', operator: 'StartsWith', value: '' }])

  let loading = $state(true)
  let notFound = $state(false)
  let saving = $state(false)
  let errorKey = $state<MessageKey | null>(null)

  let previewTotal = $state<number | null>(null)
  let previewItems = $state<DeviceDto[]>([])

  const readOnly = $derived(!auth.canEdit)

  function toInput(): GroupInput {
    return {
      name: name.trim(),
      description: description.trim() || null,
      matchMode,
      rules: rules.map((r) => ({ ...r, value: r.value.trim() })),
    }
  }

  onMount(async () => {
    if (!id) {
      loading = false
      return
    }
    try {
      const group = (await api.groups()).find((g) => g.id === id)
      if (!group) {
        notFound = true
      } else {
        name = group.name
        description = group.description ?? ''
        matchMode = group.matchMode
        rules = group.rules.map((r) => ({ ...r }))
      }
    } catch {
      notFound = true
    } finally {
      loading = false
    }
  })

  function addRule() {
    rules.push({ field: 'Name', operator: 'Contains', value: '' })
  }

  function removeRule(index: number) {
    rules.splice(index, 1)
  }

  function fieldChanged(rule: GroupRule) {
    if (rule.field === 'OsFamily') {
      rule.operator = 'Is'
      rule.value = 'Windows'
    } else if (osFamilies.includes(rule.value)) {
      rule.value = ''
    }
  }

  // Live-Vorschau (nur wenn alle Regeln einen Wert haben)
  $effect(() => {
    if (readOnly || loading) return
    const input = toInput()
    if (input.rules.length === 0 || input.rules.some((r) => r.value === '')) {
      previewTotal = null
      previewItems = []
      return
    }
    const timer = setTimeout(async () => {
      try {
        const result = await api.previewGroup(input)
        previewTotal = result.total
        previewItems = result.items
      } catch {
        previewTotal = null
        previewItems = []
      }
    }, 400)
    return () => clearTimeout(timer)
  })

  async function save(event: SubmitEvent) {
    event.preventDefault()
    saving = true
    errorKey = null
    try {
      const input = toInput()
      if (id) await api.updateGroup(id, input)
      else await api.createGroup(input)
      router.navigate('/groups')
    } catch (e) {
      errorKey = e instanceof ApiError && e.status === 409 ? 'groups.nameTaken' : 'common.saveFailed'
    } finally {
      saving = false
    }
  }
</script>

<section>
  <a class="back" href="#/groups">← {i18n.t('common.back')}</a>

  {#if loading}
    <p class="muted">{i18n.t('devices.loading')}</p>
  {:else if notFound}
    <p class="muted">{i18n.t('groups.notFound')}</p>
  {:else}
    <h1>{id ? name : i18n.t('groups.new')}</h1>

    <form class="card form" onsubmit={save}>
      <label>
        <span>{i18n.t('groups.name')}</span>
        <input bind:value={name} required maxlength="128" disabled={readOnly} />
      </label>

      <label>
        <span>{i18n.t('groups.description')}</span>
        <input bind:value={description} maxlength="1024" disabled={readOnly} />
      </label>

      <label>
        <span>{i18n.t('groups.matchMode')}</span>
        <select bind:value={matchMode} disabled={readOnly}>
          <option value="All">{i18n.t('groups.mode.All')}</option>
          <option value="Any">{i18n.t('groups.mode.Any')}</option>
        </select>
      </label>

      <fieldset>
        <legend>{i18n.t('groups.rulesTitle')}</legend>

        {#if rules.length === 0}
          <p class="muted">{i18n.t('groups.noRules')}</p>
        {/if}

        {#each rules as rule, index (index)}
          <div class="rule">
            <select bind:value={rule.field} onchange={() => fieldChanged(rule)} disabled={readOnly}>
              {#each fields as f}
                <option value={f}>{i18n.t(`field.${f}` as MessageKey)}</option>
              {/each}
            </select>

            <select bind:value={rule.operator} disabled={readOnly || rule.field === 'OsFamily'}>
              {#each operators as op}
                <option value={op}>{i18n.t(`operator.${op}` as MessageKey)}</option>
              {/each}
            </select>

            {#if rule.field === 'OsFamily'}
              <select bind:value={rule.value} disabled={readOnly}>
                {#each osFamilies as family}
                  <option value={family}>{family}</option>
                {/each}
              </select>
            {:else}
              <input
                bind:value={rule.value}
                placeholder={i18n.t('groups.value')}
                maxlength="256"
                required
                disabled={readOnly}
              />
            {/if}

            {#if !readOnly}
              <button type="button" class="ghost" onclick={() => removeRule(index)} aria-label={i18n.t('groups.removeRule')}>✕</button>
            {/if}
          </div>
        {/each}

        {#if !readOnly}
          <button type="button" onclick={addRule}>+ {i18n.t('groups.addRule')}</button>
        {/if}
      </fieldset>

      {#if previewTotal !== null}
        <div class="preview">
          <strong>{i18n.t('groups.preview', { count: previewTotal })}</strong>
          {#if previewTotal > previewItems.length}
            <span class="muted"> – {i18n.t('groups.previewMore', { count: previewItems.length })}</span>
          {/if}
          <ul>
            {#each previewItems as device (device.id)}
              <li>{device.name} <span class="muted">{device.osName} {device.osVersion}</span></li>
            {/each}
          </ul>
        </div>
      {/if}

      {#if errorKey}
        <p class="error" role="alert">{i18n.t(errorKey)}</p>
      {/if}

      {#if !readOnly}
        <div class="buttons">
          <button type="button" onclick={() => router.navigate('/groups')}>{i18n.t('common.cancel')}</button>
          <button class="primary" type="submit" disabled={saving}>{i18n.t('common.save')}</button>
        </div>
      {/if}
    </form>
  {/if}
</section>

<style>
  .back {
    color: var(--muted);
    text-decoration: none;
    font-size: 0.9rem;
  }
  h1 {
    font-size: 1.35rem;
    margin: 0.75rem 0 1rem;
  }
  .form {
    display: grid;
    gap: 1rem;
    max-width: 46rem;
  }
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  fieldset {
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 0.75rem 1rem 1rem;
    display: grid;
    gap: 0.6rem;
  }
  legend {
    font-size: 0.85rem;
    color: var(--muted);
    padding: 0 0.4rem;
  }
  .rule {
    display: grid;
    grid-template-columns: 1.2fr 1fr 1.6fr auto;
    gap: 0.5rem;
    align-items: center;
  }
  fieldset > button {
    justify-self: start;
  }
  .preview {
    font-size: 0.9rem;
  }
  .preview ul {
    margin: 0.4rem 0 0;
    padding-left: 1.2rem;
    max-height: 10rem;
    overflow-y: auto;
  }
  .buttons {
    display: flex;
    justify-content: flex-end;
    gap: 0.5rem;
  }
  .error {
    color: var(--danger);
    margin: 0;
  }
  @media (max-width: 40rem) {
    .rule {
      grid-template-columns: 1fr 1fr;
    }
  }
</style>
