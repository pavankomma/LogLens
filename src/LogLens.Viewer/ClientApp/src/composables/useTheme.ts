import { ref, watch, type Ref } from 'vue'

export function useTheme(): { theme: Ref<string> } {
  const theme = ref('')
  try {
    const t = localStorage.getItem('theme')
    theme.value = t === 'light' || t === 'dark' ? t : ''
  } catch { /* storage unavailable */ }
  watch(theme, t => {
    if (t) document.documentElement.dataset.theme = t
    else delete document.documentElement.dataset.theme
    try { localStorage.setItem('theme', t) } catch { /* storage unavailable */ }
  }, { immediate: true })
  return { theme }
}
