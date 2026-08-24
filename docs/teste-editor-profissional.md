# Roteiro de validação do editor profissional

## 1. Corrigir uma legenda de louvor

Abra um projeto de louvor, escolha um corte e altere `voce esta aki` para `Você está aqui` em **Legendas na tela**. Salve, confirme o aviso **Alterações não renderizadas**, renderize novamente e confira o MP4.

## 2. Renderizar sem legendas

Desative **Exibir legendas**, salve e renderize. Confirme que o MP4 não contém texto na tela e que o arquivo ASS não participa do filtro FFmpeg.

## 3. Criar um corte pelo vídeo original

Abra **Editor completo**, navegue até 10:30, marque a entrada, navegue até 11:25, marque a saída e crie o corte. Confirme que ele aparece na lista como manual e ainda não está renderizado.

## 4. Preservar um título manual

Edite **Título do vídeo**, salve e renderize. Reabra o projeto e confirme o mesmo título. Use **Sugerir novo título** apenas para verificar que as opções não alteram o campo sem escolha.

## 5. Entender poucos candidatos

Abra um projeto que tenha gerado menos cortes do que o solicitado. Confira os contadores reais no diagnóstico e use **Criar cortes no Editor completo** para adicionar candidatos manualmente.
