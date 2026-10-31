# FreeWalk (Caminhar a pé)

Levante do banco do motorista, ande pelo ônibus, desça pela porta e ande pelo mapa em primeira pessoa.

- **Levantar:** com o ônibus parado, aperte G. Você vai para o ponto do salão mais perto do banco (os mesmos caminhos
  que os passageiros do jogo usam), olhando para a porta mais próxima. O jogo puxa o freio de mão.
- **Portas:** olhando para uma porta de perto, por dentro ou por fora, aparece sobre ela "E  Abrir porta frontal"
  (ou do meio, traseira). E abre ou fecha aquela porta. Sem ar no reservatório, a porta não abre e aparece um aviso.
- **Dentro do ônibus:** você anda pelo corredor e pelas escadas das portas, subindo e descendo os degraus. Com a porta
  aberta, é só andar por ela para descer, e para subir de volta. Com a porta fechada, ela barra a passagem.
- **Fora:** W/A/S/D andam, Shift corre, Espaço pula, Ctrl agacha. O mouse olha em volta.
- **Dirigir de novo:** entre no ônibus, vá até o posto do motorista e aperte G (aparece o aviso "G: dirigir"). Do lado
  de fora, G não faz nada.
- **Esc** continua pausando o jogo.

A pé, os controles de teclado do jogo ficam suspensos: W não acelera o ônibus e as letras não acionam comandos.

## Teclas

| Tecla | Ação |
|---|---|
| G | Levanta do banco / volta a dirigir |
| E | Abre ou fecha a porta para a qual você está olhando |
| W / A / S / D | Andar |
| Shift esquerdo (segurar) | Correr |
| Espaço | Pular |
| Ctrl esquerdo (segurar) | Agachar |

Todas ficam na página **Atalhos** da PBSLoaderCommonInterface (ou em `[Atalhos]` no `.cfg`). As teclas de andar e o E só valem a pé,
por isso não aparecem como conflito com as teclas do jogo.

## Opções

As opções ficam na categoria `[AndarAPe]`:

| Opção | Efeito |
|---|---|
| `VelocidadeAndar` / `VelocidadeCorrer` / `VelocidadeAgachado` | Velocidades, em m/s |
| `AlturaOlhos` | Altura dos olhos em pé, em metros |
| `ForcaPulo` | Altura do pulo, em metros (dentro do ônibus, no máximo 0,35) |
| `Sensibilidade` / `InverterY` | Mouse |
| `CampoDeVisao` | Campo de visão a pé. 0 usa o da câmera do jogo |
| `Balanco` | Balanço da cabeça ao andar. 0 desliga |
| `DistanciaParaEntrar` | Só em ônibus sem caminhos de passageiros: distância máxima de uma porta para G levar ao banco |
| `VelocidadeMaximaParaDescer` | Acima desta velocidade (km/h) não dá para levantar do banco |

## Como funciona o interior

O ônibus não tem colisão de piso nem de bancos. O mod usa os caminhos dos passageiros que vêm no modelo do ônibus
(objetos como `_r2y11_`: porta R2, caminho y, 11º ponto), que vão da calçada, pelos degraus, até o corredor:

- **Altura do piso:** a do caminho mais perto, com os degraus.
- **Onde dá para andar:** até 45 cm de algum caminho, dentro da caixa da carroceria. Por isso não dá para atravessar
  bancos nem ir até o para-brisa.
- **Faixa de cada porta:** a dos pontos junto à parede.

Dentro, você anda nas coordenadas do ônibus: se ele se mexer, você vai junto.

## Problemas conhecidos

- **Ônibus sem caminhos de passageiros:** ao levantar você vai direto para fora, ao lado da porta da frente, e não dá
  para andar dentro dele. Nesses ônibus, G perto de uma porta leva de volta ao banco.
- **Lugares do mapa sem colisão no chão:** se você cair por um deles, volta para o último chão firme.
- **Banco do motorista:** fica vazio enquanto você anda a pé.

## Desenvolvimento

`--pbsloadercommoninterface-partida --pbsloadercommoninterface-onibus=1519 --andarape-teste --andarape-sair` faz o roteiro inteiro:
1. levanta do banco e anda até a porta frontal fechada, que precisa barrar a passagem;
2. abre a porta pelo aviso;
3. desce para a calçada e volta pela mesma porta;
4. anda até o banco e volta a dirigir.

Grava `UserData/AndarAPe/teste.txt` e capturas `teste_*.png`. `--andarape-pontos` só grava os caminhos e as portas.
