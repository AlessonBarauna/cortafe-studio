# CortaFé Studio

Estúdio local para transformar pregações, ministrações, podcasts e aulas em cortes verticais com transcrição, legendas, capas e textos de postagem. O processamento fica no computador e não utiliza APIs pagas.

## Recursos entregues

- Upload de vídeo ou áudio e importação por link público do YouTube.
- Fila persistente e acompanhamento do processamento pela interface.
- Retomada automática de trabalhos interrompidos e bloqueio de entradas duplicadas na fila.
- Checkpoints de mídia, áudio, transcrição e análise com reprocessamento seletivo.
- Transcrição em português com Faster-Whisper, limites refinados por palavra e remoção de muletas no início do corte.
- Seleção automática de trechos por duração, completude e palavras de impacto.
- Aprendizado editorial local e explicável com base nos cortes aprovados e rejeitados.
- Título, frase de capa, legenda e hashtags via Ollama local, com fallback sem IA.
- Capas verticais extraídas do próprio vídeo.
- Mesa de revisão para editar título, texto, início e fim de cada corte.
- Direção visual por corte: foco do enquadramento, três estilos de legenda, frame, cor e posição da capa.
- Detecção local de rostos com OpenCV, foco horizontal automático e composição com fundo desfocado.
- Linha do tempo com forma de onda, ajuste visual de início e fim, edição da transcrição, duplicação e divisão de cortes.
- Renderização 9:16 em MP4 com legendas dinâmicas, destaque sincronizado da palavra falada e download individual.
- Biblioteca local de projetos, sem conta e sem telemetria.

## Requisitos

- Windows 10/11 ou outro sistema compatível com .NET 8.
- .NET SDK 8.
- Python 3.11 ou 3.12.
- FFmpeg e FFprobe.
- yt-dlp para links do YouTube.
- Node.js 22 ou superior para resolver os desafios JavaScript atuais do YouTube.
- Ollama opcional. Sem ele, títulos padrão continuam disponíveis.

## Instalação no Windows

Abra o PowerShell na raiz do projeto:

```powershell
.\scripts\instalar-windows.ps1
```

Se o FFmpeg não estiver instalado:

```powershell
winget install Gyan.FFmpeg
```

Para os textos criativos locais, instale o Ollama e baixe o modelo utilizado:

```powershell
ollama pull qwen2.5:3b
```

O Ollama é opcional e pode ser instalado posteriormente.

## Executar

```powershell
.\scripts\iniciar-windows.ps1
```

Abra `http://localhost:5088`. A barra superior informa se as ferramentas foram detectadas.

Para validar backend, frontend e a API local após uma atualização:

```powershell
.\scripts\verificar-projeto.ps1
```

O botão **Diagnóstico** mostra ferramentas instaladas, espaço em disco, uso do armazenamento, fila e alertas sem revelar credenciais.

O diagnóstico consulta atualizações do yt-dlp e permite atualizar com backup, teste de versão e restauração automática em caso de falha.

## Modelos de transcrição

- `base`: opção mais rápida para CPU.
- `small`: mais preciso, mas pode demorar em vídeos longos sem GPU.
- `medium`: equilíbrio entre velocidade e precisão.
- `large-v3`: melhor precisão, mas exige mais memória e tempo.

Na primeira utilização, o Faster-Whisper baixa o modelo selecionado. Isso acontece apenas uma vez por modelo.

## Armazenamento e privacidade

O servidor aceita conexões apenas do próprio computador. É possível configurar um PIN local e criar backups criptografados em `storage/backups`; os vídeos não entram no backup por padrão.

Os projetos ficam em `src/CortaFeStudio.Api/storage/projects`. Cada projeto contém o original temporário, áudio de análise, transcrição, capas, legendas e cortes renderizados. Essa pasta está ignorada pelo Git.

O sistema destina-se a conteúdo próprio ou autorizado. Links privados, transmissões ao vivo, vídeos de membros e conteúdos com restrições podem não funcionar. O YouTube pode alterar seus mecanismos; mantenha o yt-dlp atualizado.

## Arquitetura

- ASP.NET Core 8 Minimal API.
- Worker nativo com `BackgroundService` e `Channel`.
- Catálogo transacional em SQLite, com JSON por projeto mantido como backup portátil.
- Limpeza de temporários, cálculo de espaço ocupado e arquivamento de projetos.
- Frontend responsivo em HTML, JavaScript e Bootstrap 5.3 customizado.
- FFmpeg/FFprobe para mídia e ASS para legendas.
- Faster-Whisper para fala-texto.
- Ollama para enriquecimento editorial opcional.

## Limitações atuais

- O reenquadramento usa corte central inteligente ao formato 9:16; acompanhamento contínuo de rosto e diarização são evoluções futuras.
- A transcrição de canto com instrumentos altos pode exigir correção manual.
- Os candidatos e suas capas ficam prontos primeiro; os MP4 são renderizados após a revisão, evitando gastar processamento com sugestões descartadas.
- A capa usa um frame tratado do vídeo. Remoção de fundo e editor livre de composição podem ser adicionados depois.

## Conectar redes sociais

Abra a opção **Publicar** no topo do CortaFé. As credenciais e tokens são protegidos localmente pelo ASP.NET Data Protection e não devem ser enviados por mensagem nem adicionados ao Git.

Cadastre estes retornos nos aplicativos de desenvolvedor:

```text
YouTube:   http://localhost:5088/api/social/callback/youtube
Instagram: https://SEU-DOMINIO/api/social/callback/instagram
TikTok:    https://SEU-DOMINIO/api/social/callback/tiktok
```

- **YouTube:** habilite YouTube Data API v3 no Google Cloud, crie um cliente OAuth e adicione o usuário como testador enquanto o consentimento estiver em teste.
- **Instagram:** use uma conta profissional, crie um aplicativo Meta/Instagram, solicite `instagram_business_basic` e `instagram_business_content_publish` e forneça uma URL HTTPS pública para o MP4.
- **TikTok:** crie um aplicativo no TikTok for Developers e solicite Login Kit e Content Posting API com `video.publish`. Aplicativos não auditados publicam somente como `SELF_ONLY`.

Cada corte renderizado passa a exibir botões YouTube, Instagram e TikTok. A primeira publicação deve ser privada para validar título, legenda e enquadramento.

As publicações podem ser agendadas, sobrevivem à reinicialização do aplicativo e falhas podem ser reenviadas pela Central de Publicação. O sistema impede duplicidade do mesmo corte na mesma rede.

Uploads para o YouTube usam blocos retomáveis de 8 MB, persistem o progresso e podem continuar após uma interrupção. O histórico também consulta o estado de processamento final do vídeo.
