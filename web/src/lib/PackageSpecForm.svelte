<script lang="ts">
  import type { PackageKind } from './api'
  import { i18n, type MessageKey } from './i18n.svelte'
  import type { SpecForm } from './spec'

  let {
    kind,
    spec = $bindable(),
    secretNames = [],
    platformLocked = false,
  }: { kind: PackageKind; spec: SpecForm; secretNames?: string[]; platformLocked?: boolean } = $props()

  const isRepo = $derived(kind !== 'Psadt')
  const versionKey = $derived(
    (kind === 'Flatpak' || kind === 'Snap' ? `sw.spec.version.${kind}` : 'sw.spec.version') as MessageKey,
  )
</script>

{#if kind === 'Chocolatey' || kind === 'Flatpak' || kind === 'Snap'}
  <p class="hint" role="note">{i18n.t('sw.manager.hint', { name: kind === 'Chocolatey' ? 'Chocolatey' : kind })}</p>
{/if}

<div class="grid">
  {#if !isRepo}
    <label>
      <span>{i18n.t('sw.spec.platform')}</span>
      <select bind:value={spec.platform} disabled={platformLocked}>
        <option value="Windows">Windows</option>
        <option value="Linux">Linux</option>
      </select>
    </label>
  {/if}
  {#if isRepo}
    <label class="wide">
      <span>{i18n.t(`sw.spec.identifier.${kind}` as MessageKey)}</span>
      <input bind:value={spec.identifier} required maxlength="200" />
    </label>

    <label>
      <span>{i18n.t(versionKey)}</span>
      <input bind:value={spec.version} maxlength="64" />
    </label>

    {#if kind === 'Winget'}
      <label>
        <span>{i18n.t('sw.spec.scope')}</span>
        <select bind:value={spec.scope}>
          <option value="machine">{i18n.t('sw.spec.scope.machine')}</option>
          <option value="user">{i18n.t('sw.spec.scope.user')}</option>
        </select>
      </label>
    {/if}

    {#if kind === 'Flatpak'}
      <label>
        <span>{i18n.t('sw.spec.scope')}</span>
        <select bind:value={spec.scope}>
          <option value="system">{i18n.t('sw.spec.scope.system')}</option>
          <option value="user">{i18n.t('sw.spec.scope.user')}</option>
        </select>
      </label>
      <label>
        <span>{i18n.t('sw.spec.remote')}</span>
        <input bind:value={spec.remote} maxlength="64" />
      </label>
    {/if}

    {#if kind === 'Winget' || kind === 'Chocolatey'}
      <label>
        <span>{i18n.t('sw.spec.source')}</span>
        <input bind:value={spec.source} maxlength="200" />
      </label>
    {/if}

    {#if kind === 'Snap'}
      <label>
        <span>{i18n.t('sw.spec.channel')}</span>
        <input bind:value={spec.channel} maxlength="64" />
      </label>
      <label class="check">
        <input type="checkbox" bind:checked={spec.classic} />
        {i18n.t('sw.spec.classic')}
      </label>
    {/if}
  {/if}

  <label class="wide">
    <span>{i18n.t('sw.spec.arguments')}</span>
    <input bind:value={spec.arguments} maxlength="1000" autocomplete="off" />
  </label>
</div>

<p class="muted small">
  {isRepo ? i18n.t('sw.spec.argumentsHint') : i18n.t('sw.spec.argumentsPsadt')}
  {#if !isRepo && spec.platform === 'Linux'}{i18n.t('sw.spec.platformLinuxHint')}{/if}
  {#if secretNames.length > 0}
    {i18n.t('sw.secrets.available')}: {secretNames.map((n) => `{{secret:${n}}}`).join(', ')}
  {/if}
</p>
{#if isRepo}
  <p class="muted small">{i18n.t('sw.spec.unattended')} {i18n.t('sw.spec.autoUpdate')}</p>
{/if}

<style>
  .hint {
    margin: 0 0 0.75rem;
    padding: 0.5rem 0.75rem;
    border-left: 3px solid var(--accent, #3b82f6);
    background: var(--surface-2, rgba(127, 127, 127, 0.1));
    font-size: 0.9rem;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
    gap: 0.75rem;
  }
  .wide {
    grid-column: 1 / -1;
  }
  label {
    display: grid;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--muted);
  }
  label.check {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    color: var(--text);
  }
  .small {
    font-size: 0.85rem;
    margin: 0.4rem 0 0;
  }
</style>