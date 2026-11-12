#!/usr/bin/env bash
# Instala ou atualiza o Cortafé Studio neste servidor, sem mexer em outros containers ou sistemas.
# Uso: bash instalar-servidor.sh   (ou: curl -fsSL <raw do GitHub> | bash)
set -euo pipefail

DIR="${CORTAFE_DIR:-$HOME/cortafe-studio}"
REPO="https://github.com/AlessonBarauna/cortafe-studio"
PORT=5088

command -v docker >/dev/null || { echo "Docker não encontrado. Instale com: curl -fsSL https://get.docker.com | sh"; exit 1; }
docker info >/dev/null 2>&1 || { echo "Sem permissão no Docker. Rode com sudo ou adicione seu usuário ao grupo docker."; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "Plugin 'docker compose' não encontrado. Instale: sudo apt install docker-compose-plugin"; exit 1; }
command -v git >/dev/null || { echo "git não encontrado. Instale com: sudo apt install git"; exit 1; }

if [ -d "$DIR/.git" ]; then
  git -C "$DIR" pull --ff-only
else
  git clone "$REPO" "$DIR"
fi
cd "$DIR"

# IP em que a porta será publicada: o da VPN Tailscale, se houver; senão só o próprio servidor.
BIND="${CORTAFE_BIND:-}"
if [ -z "$BIND" ] && command -v tailscale >/dev/null 2>&1; then BIND="$(tailscale ip -4 2>/dev/null | head -1 || true)"; fi
BIND="${BIND:-127.0.0.1}"

# Não atropela outro sistema que já use a porta (a menos que seja o próprio Cortafé).
if ss -ltn 2>/dev/null | awk '{print $4}' | grep -qE "[:.]${PORT}$" && ! docker ps --format '{{.Names}}' | grep -qx cortafe-studio; then
  echo "A porta ${PORT} já está em uso por outro programa. Libere-a ou ajuste a porta no docker-compose.yml."
  exit 1
fi

echo "CORTAFE_BIND=${BIND}" > .env
docker compose -p cortafe up -d --build

echo
echo "Cortafé Studio no ar. Acesse: http://${BIND}:${PORT}"
echo "Na primeira vez, configure o PIN do estúdio."
echo "Logs: docker compose -p cortafe -f ${DIR}/docker-compose.yml logs -f"
