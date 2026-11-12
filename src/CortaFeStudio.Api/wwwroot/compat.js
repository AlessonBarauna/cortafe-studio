// Compatibilidade para acesso por HTTP em rede privada/VPN: nesse caso o navegador não considera a página
// um "contexto seguro" e remove crypto.randomUUID e navigator.clipboard, que o estúdio usa.
(() => {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID !== 'function') {
    crypto.randomUUID = () => {
      const bytes = crypto.getRandomValues(new Uint8Array(16));
      bytes[6] = (bytes[6] & 0x0f) | 0x40;
      bytes[8] = (bytes[8] & 0x3f) | 0x80;
      const hex = [...bytes].map(b => b.toString(16).padStart(2, '0'));
      return `${hex.slice(0, 4).join('')}-${hex.slice(4, 6).join('')}-${hex.slice(6, 8).join('')}-${hex.slice(8, 10).join('')}-${hex.slice(10).join('')}`;
    };
  }
  if (!navigator.clipboard) {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: text => new Promise((resolve, reject) => {
          const area = document.createElement('textarea');
          area.value = text;
          area.setAttribute('readonly', '');
          area.style.cssText = 'position:fixed;top:-1000px;opacity:0';
          document.body.append(area);
          area.select();
          const ok = document.execCommand('copy');
          area.remove();
          ok ? resolve() : reject(new Error('Não foi possível copiar.'));
        })
      }
    });
  }
})();
