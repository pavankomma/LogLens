<script setup lang="ts">
import ModalDialog from './ModalDialog.vue'

defineEmits<{ close: [] }>()
</script>

<template>
  <ModalDialog label="Search tips" @close="$emit('close')">
    <h3>Search tips</h3>
    <p class="dim" style="margin:0 0 10px">Type words to search messages. Start typing a field name (e.g. <code>Order</code>) and pick a suggestion, or use <b>+ Add filter</b>. Short form for power users:</p>
    <table>
      <tr><td><code>payment failed</code></td><td>free text (all words must match; quotes for phrases)</td></tr>
      <tr><td><code>-health</code></td><td>exclude any term</td></tr>
      <tr><td><code>level:error</code> · <code>level:warning+</code></td><td>exact level · that level and above</td></tr>
      <tr><td><code>category:Shop.Orders</code> · <code>category:Shop.*</code></td><td>exact category · wildcard at either end</td></tr>
      <tr><td><code>OrderId=1002</code> · <code>Status!=200</code></td><td>property equals / not equals</td></tr>
      <tr><td><code>User~admin</code></td><td>property contains</td></tr>
      <tr><td><code>Elapsed&gt;500</code> · <code>Pct&lt;=90</code></td><td>numeric compare</td></tr>
      <tr><td><code>has:RequestId</code> · <code>has:exception</code></td><td>property present / has exception</td></tr>
    </table>
    <p class="dim" style="margin:10px 0 0">Keys: <kbd>/</kbd> search · <kbd>j</kbd>/<kbd>k</kbd> move · <kbd>Enter</kbd> details · <kbd>l</kbd> Live · <kbd>Esc</kbd> close</p>
    <div class="actions"><button class="btn" autofocus @click="$emit('close')">Close</button></div>
  </ModalDialog>
</template>
