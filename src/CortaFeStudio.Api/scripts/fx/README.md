# Cortafé FX

Transforma um corte vertical (1080x1920) em vídeo animado, tudo local e sem APIs pagas:

- **Legenda cinética:** palavra a palavra, com destaque dourado na palavra falada e "slam" nas palavras de impacto.
- **Gancho:** a frase mais forte do corte abre o vídeo em texto grande (primeiros 3 segundos).
- **Ilustrações temáticas:** luz, cruz, fogo e tempestade aparecem quando a palavra correspondente é dita.
- **Câmera:** zoom, tremida e flash nos momentos de impacto.
- **Sons:** impactos, whoosh, trovão, sino, gongo e uma cama atmosférica que abaixa quando o pregador fala.

## Uso direto

```powershell
python scripts\fx\cortafe_fx.py --video corte.mp4 --words palavras.json --out animado.mp4
```

`palavras.json` aceita a lista de `{word,start,end}` ou os segmentos do `transcript.json` do Cortafé (tempos em segundos, relativos ao corte).

Opções úteis: `--hook "TEXTO"`, `--no-hook`, `--no-themes`, `--no-sfx`, `--watermark "TEXTO"`, `--plan plano.json`, `--max-seconds 10` (teste rápido) e `--snap 1.0,4.5 --snapdir quadros` (salva quadros PNG).

## No estúdio

O render de cada corte usa o Cortafé FX automaticamente quando `AnimatedFx` está ligado (padrão), o corte é vertical, não é louvor e não teve silêncios removidos. Nesse caso o corte-base sai sem a legenda ASS e sem a moldura, e o módulo desenha a legenda animada, a marca d'água e os efeitos. Se o Python não tiver as dependências (`scripts\fx\requirements.txt`) ou o módulo falhar, o render clássico é refeito sozinho.

Dependências: `skia-python`, `numpy`, `scipy`, `pillow` (o `instalar-windows.ps1` já instala).
