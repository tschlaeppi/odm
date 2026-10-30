<script lang="ts">
  import type { GroupDto, Rollout, RolloutMode } from './api'
  import { i18n, type MessageKey } from './i18n.svelte'

  // Tranchenplan einer Zuweisung; null = sofort an alle. Steht in einem <form>, die Browserprüfung (required, min, max) greift also.
  let { value = $bindable(null), groups }: { value: Rollout | null; groups: GroupDto[] } = $props()

  const mode = $derived<RolloutMode | ''>(value?.mode ?? '')

  function setMode(next: string) {
    if (next === 'Percent') {
      value = {
        mode: 'Percent',
        stages: [
          { afterDays: 5, percent: 20, groupId: null },
          { afterDays: 10, percent: 40, groupId: null },
          { afterDays: 15, percent: 100, groupId: null },
        ],
      }
    } else if (next === 'Groups') {
      value = { mode: 'Groups', stages: [{ afterDays: 0, percent: null, groupId: null }] }
    } else {
      value = null
    }
  }

  function addStage() {
    if (!value) return
    const last = value.stages[value.stages.length - 1]
    const days = Math.min((last?.afterDays ?? 0) + 5, 365)
    value.stages.push(
      value.mode === 'Percent'
        ? { afterDays: days, percent: 100, groupId: null }
        : { afterDays: days, percent: null, groupId: null },
    )
  }

  function removeStage(index: number) {
    if (value && value.stages.length > 1) value.stages.splice(index, 1)
  }
</script>

<fieldset class="rollout">
  <legend>{i18n.t('rollout.title')}</legend>

  <label>
    <span class="sr">{i18n.t('rollout.title')}</span>
    <select value={mode} onchange={(e) => setMode(e.currentTarget.value)}>
      <option value="">{i18n.t('rollout.mode.none')}</option>
      <option value="Percent">{i18n.t('rollout.mode.Percent')}</option>
      <option value="Groups">{i18n.t('rollout.mode.Groups')}</option>
    </select>
  </label>

  {#if value}
    <div class="stages">
      {#each value.stages as stage, index (index)}
        <div class="stage">
          <label>
            <span>{i18n.t('rollout.afterDays')}</span>
            <input type="number" min="0" max="365" step="1" required bind:value={stage.afterDays} />
          </label>

          {#if value.mode === 'Percent'}
            <label>
              <span>{i18n.t('rollout.percent')}</span>
              <input type="number" min="1" max="100" step="1" required bind:value={stage.percent} />
            </label>
          {:else}
            <label class="grow">
              <span>{i18n.t('rollout.group')}</span>
              <select required bind:value={stage.groupId}>
                <option value={null} disabled>–</option>
                {#each groups as g (g.id)}
                  <option value={g.id}>{g.name} ({g.deviceCount})</option>
                {/each}
              </select>
            </label>
          {/if}

          <button
            type="button"
            class="ghost"
            disabled={value.stages.length <= 1}
            onclick={() => removeStage(index)}
            aria-label={i18n.t('rollout.removeStage')}
          >
            ✕
          </button>
        </div>
      {/each}
    </div>

    <div class="foot">
      <button type="button" class="ghost" onclick={addStage}>+ {i18n.t('rollout.addStage')}</button>
      <p class="muted small">{i18n.t(`rollout.hint.${value.mode}` as MessageKey)}</p>
    </div>
  {/if}
</fieldset>

<style>
  .rollout {
    border: 1px solid var(--border);
    border-radius: 0.5rem;
    padding: 0.6rem 0.8rem 0.8rem;
    margin: 0;
    min-width: 0;
    display: grid;
    gap: 0.6rem;
  }
  legend {
    font-size: 0.85rem;
    color: var(--muted);
    padding: 0 0.35rem;
  }
  .sr {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip-path: inset(50%);
  }
  .stages {
    display: grid;
    gap: 0.5rem;
  }
  .stage {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: end;
  }
  .grow {
    flex: 1;
    min-width: 11rem;
  }
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  .foot {
    display: flex;
    flex-wrap: wrap;
    gap: 0.75rem;
    align-items: center;
  }
  .small {
    font-size: 0.85rem;
    margin: 0;
    flex: 1;
    min-width: 14rem;
  }
</style>
