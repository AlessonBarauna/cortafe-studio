# Cortafé Studio no servidor (Linux + Docker)

## Atalho

No servidor: `curl -fsSL https://raw.githubusercontent.com/AlessonBarauna/cortafe-studio/main/deploy/instalar-servidor.sh | bash`. O script confere Docker e porta, publica no IP do Tailscale (se houver) e sobe o app sem mexer em outros containers. Rode o mesmo comando de novo para atualizar. Os passos manuais estão abaixo.

## 1. Preparar o servidor (uma vez)

```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER   # saia e entre de novo na sessão
git clone https://github.com/AlessonBarauna/cortafe-studio && cd cortafe-studio
```

## 2. Subir

```bash
docker compose up -d --build
docker compose logs -f          # acompanhar; Ctrl+C sai dos logs sem parar o app
```

A primeira construção demora (baixa .NET, FFmpeg e pacotes Python). O modelo do Whisper é baixado na primeira transcrição e fica guardado no volume.

## 3. Acessar (rede privada / VPN)

Por padrão a porta só abre no próprio servidor. Do seu computador, abra um túnel SSH:

```bash
ssh -L 5088:localhost:5088 usuario@IP_DO_SERVIDOR
```

e acesse `http://localhost:5088`.

Para acessar direto pela VPN (Tailscale, WireGuard etc.), publique a porta no IP do servidor na VPN:

```bash
CORTAFE_BIND=100.x.y.z docker compose up -d
```

e acesse `http://100.x.y.z:5088`. O app aceita só loopback e redes privadas (10.x, 172.16-31.x, 192.168.x, 100.64-127.x); IP público é sempre recusado.

**Importante:** configure o PIN do estúdio na primeira vez que abrir (opção de segurança do app). Com acesso por rede, ele é a única senha.

## 4. Dia a dia

```bash
docker compose logs --tail 100     # ver o que aconteceu
docker compose restart             # reiniciar
git pull && docker compose up -d --build   # atualizar para uma versão nova
```

Projetos, banco, PIN e chaves ficam no volume `cortafe-storage` e sobrevivem a atualizações. Backup do volume:

```bash
docker run --rm -v cortafe-storage:/d -v $PWD:/b alpine tar czf /b/cortafe-storage.tgz -C /d .
```

## Observações

- Sem GPU, a transcrição usa CPU: prefira os modelos `base` ou `small` (o `large-v3` fica muito lento).
- Redes sociais (YouTube, Instagram, TikTok): os endereços de retorno (OAuth) precisam apontar para um endereço que você consiga abrir no navegador, como `http://localhost:5088` com o túnel ligado. Instagram e TikTok exigem URL pública HTTPS, o que este modo (só VPN) não oferece.
