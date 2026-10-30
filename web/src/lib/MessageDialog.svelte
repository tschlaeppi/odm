<script lang="ts">
  import { i18n } from './i18n.svelte'

  let dialog: HTMLDialogElement
  let title = $state('')
  let text = $state('')
  let copied = $state(false)

  export function show(heading: string, message: string | null) {
    title = heading
    text = message ?? ''
    copied = false
    dialog.showModal()
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(text)
      copied = true
    } catch {
      /* Zwischenablage nicht verfügbar */
    }
  }
</script>

<dialog bind:this={dialog}>
  <div class="dialog-form wide">
    <h2>{title}</h2>
    <pre>{text || '–'}</pre>
    <div class="buttons">
      <button class="ghost" onclick={copy}>{copied ? i18n.t('enroll.copied') : i18n.t('enroll.copy')}</button>
      <button class="primary" onclick={() => dialog.close()}>{i18n.t('common.close')}</button>
    </div>
  </div>
</dialog>

<style>
  dialog {
    width: min(60rem, 95vw);
  }
  pre {
    margin: 0;
    max-height: 60vh;
    overflow: auto;
    padding: 0.75rem;
    background: var(--hover);
    border-radius: 0.25rem;
    font-size: 0.8rem;
    white-space: pre-wrap;
    overflow-wrap: anywhere;
  }
</style>
