const profileDurations = { pregacao:[40,90], louvor:[25,60], podcast:[35,90], aula:[30,75], motivacao:[20,50], negocios:[25,60], tecnologia:[30,90] };
document.addEventListener('change', event => {
  if (event.target?.name !== 'contentType') return;
  const [min, max] = profileDurations[event.target.value] || [30,75];
  const form = event.target.closest('form'); if (!form) return;
  form.elements.minDuration.value = min; form.elements.maxDuration.value = max;
  toast(`Duração ajustada para ${min}–${max} segundos`);
});
