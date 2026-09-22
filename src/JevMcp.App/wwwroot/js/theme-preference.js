(function () {
  const storageKey = 'javmcp.theme';

  function applyDocumentTheme(dark) {
    if (!document?.body) {
      return;
    }

    document.body.classList.toggle('mud-theme-dark', dark);
    document.body.classList.toggle('mud-theme-light', !dark);
  }

  window.javmcpTheme = {
    isDark() {
      try {
        const dark = localStorage.getItem(storageKey) === 'dark';
        applyDocumentTheme(dark);
        return dark;
      } catch {
        return false;
      }
    },
    setDark(dark) {
      try {
        localStorage.setItem(storageKey, dark ? 'dark' : 'light');
      } catch {
        // Private mode or blocked storage — preference lasts for the session only.
      }

      applyDocumentTheme(dark);
    },
  };
})();
