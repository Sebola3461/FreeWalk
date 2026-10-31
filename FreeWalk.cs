using System;
using MelonLoader;
using PBSLoaderCommonUtils;
using UnityEngine;

[assembly: MelonInfo(typeof(AndarAPe.AndarAPeMod), "FreeWalk", "0.2.0", "nzrn")]
[assembly: MelonGame(null, "Proton Bus Simulator")]

namespace AndarAPe
{
    // Descer do ônibus e andar pelo mapa em primeira pessoa.
    // O jogo não tem personagem: o mod põe a câmera do jogo na câmera livre (MepMobileControls.ClicouFreeCameraLivre,
    // que também puxa o freio de mão), desliga o MouseOrbitCSHARP (Harmony em Update/LateUpdate) e passa a mover a
    // câmera a partir de um CharacterController próprio. Para voltar, MepMobileControls.SetCameraMotorista.
    // Continuar usando a câmera do jogo mantém o pós-processamento, o AudioListener e a distância de desenho
    // (os scripts de LOD medem a distância até a câmera).
    // Módulos:
    //   Pedestre  descer, andar, subir (fora: CharacterController; dentro: coordenadas do ônibus)
    //   Interior  piso, paredes e portas do ônibus pelos caminhos dos passageiros
    //   Interacao aviso "Abrir porta" preso na porta
    //   Patches   desliga o MouseOrbitCSHARP enquanto anda
    // Linha de comando: --andarape-teste desce, anda, pula e sobe sozinho e grava teste.txt;
    // --andarape-sair fecha o jogo depois.
    public class AndarAPeMod : MelonMod
    {
        public override void OnInitializeMelon() => Cfg.Criar();

        public override void OnLateInitializeMelon() => UiIntegracao.Registrar();

        public override void OnSceneWasInitialized(int buildIndex, string sceneName) => Seguro(Pedestre.CenaMudou, "CenaMudou");

        public override void OnUpdate()
        {
            Seguro(Pedestre.Quadro, "Quadro");
            // A pé, o MouseOrbitCSHARP fica parado (ele move a câmera e, na câmera livre, lê W/A/S/D direto do
            // Input); quem posiciona a câmera é o Pedestre.
            Vista.Assumir("FreeWalk", Pedestre.Andando);
            Seguro(Teste.Quadro, "Teste");
        }

        public override void OnLateUpdate() => Seguro(Pedestre.PosicionarCamera, "Camera");

        public override void OnGUI() => Seguro(Interacao.Desenhar, "Interacao");

        internal static void Seguro(Action a, string onde)
        {
            try { a(); }
            catch (Exception e) { Log.Erro(onde, e); }
        }
    }

    internal static class Log
    {
        static readonly System.Collections.Generic.Dictionary<string, float> _ultimo = new();
        static readonly System.Collections.Generic.HashSet<string> _comPilha = new();
        internal static void Msg(string s) => Melon<AndarAPeMod>.Logger.Msg(s);
        internal static void Aviso(string s) => Melon<AndarAPeMod>.Logger.Warning(s);
        // Erros repetidos (todo frame) aparecem no máximo a cada 10 s; a pilha só na primeira vez.
        internal static void Erro(string onde, Exception e)
        {
            float agora = Time.realtimeSinceStartup;
            if (_ultimo.TryGetValue(onde, out float t) && agora - t < 10f) return;
            _ultimo[onde] = agora;
            bool primeiro = _comPilha.Add(onde);
            Melon<AndarAPeMod>.Logger.Warning(primeiro ? $"{onde}: {e}" : $"{onde}: {e.GetType().Name}: {e.Message}");
        }
    }

    internal static class Cfg
    {
        internal const string M = "FreeWalk";
        // Descer/subir é global. As teclas de andar só valem a pé, com os controles do jogo suspensos,
        // por isso são registradas como "de painel": não conflitam com W/A/S/D do jogo.
        internal static Atalho TeclaDescer, Interagir, Frente, Tras, Esquerda, Direita, Correr, Pular, Agachar;
        internal static MelonPreferences_Entry<float> VelocidadeAndar, VelocidadeCorrer, VelocidadeAgachado, AlturaOlhos,
            ForcaPulo, Sensibilidade, CampoDeVisao, DistanciaParaEntrar, VelocidadeMaximaParaDescer, Balanco;
        internal static MelonPreferences_Entry<bool> InverterY;

        internal static void Criar()
        {
            var c = MelonPreferences.CreateCategory(M, "AndarAPe (andar a pé pelo mapa)");
            TeclaDescer = Atalhos.Registrar(M, "Descer", "Descer / subir no ônibus", "G", Pedestre.Alternar,
                "Sai do banco do motorista para o salão (ônibus parado), ou volta a dirigir no posto do motorista");
            Interagir = Atalhos.Registrar(M, "Interagir", "Abrir / fechar a porta", "E",
                descricao: "Olhando para uma porta do ônibus, de perto", soComPainel: true);
            Frente = Atalhos.Registrar(M, "Frente", "Andar para frente", "W", deSegurar: true, soComPainel: true);
            Tras = Atalhos.Registrar(M, "Tras", "Andar para trás", "S", deSegurar: true, soComPainel: true);
            Esquerda = Atalhos.Registrar(M, "Esquerda", "Passo para a esquerda", "A", deSegurar: true, soComPainel: true);
            Direita = Atalhos.Registrar(M, "Direita", "Passo para a direita", "D", deSegurar: true, soComPainel: true);
            Correr = Atalhos.Registrar(M, "Correr", "Correr (segurar)", "LeftShift", deSegurar: true, soComPainel: true);
            Pular = Atalhos.Registrar(M, "Pular", "Pular", "Space", deSegurar: true, soComPainel: true);
            Agachar = Atalhos.Registrar(M, "Agachar", "Agachar (segurar)", "LeftControl", deSegurar: true, soComPainel: true);
            VelocidadeAndar = c.CreateEntry("VelocidadeAndar", 1.5f, description: "Velocidade andando (m/s)");
            VelocidadeCorrer = c.CreateEntry("VelocidadeCorrer", 4.5f, description: "Velocidade correndo (m/s)");
            VelocidadeAgachado = c.CreateEntry("VelocidadeAgachado", 0.8f, description: "Velocidade agachado (m/s)");
            AlturaOlhos = c.CreateEntry("AlturaOlhos", 1.55f, description: "Altura dos olhos em pé (m). 1,55 = pessoa de ~1,67 m");
            // v0.2 tinha 1,65 de padrão (parecia alto demais): quem não mexeu passa para o novo.
            if (Mathf.Abs(AlturaOlhos.Value - 1.65f) < 0.001f) AlturaOlhos.Value = 1.55f;
            ForcaPulo = c.CreateEntry("ForcaPulo", 0.45f, description: "Altura do pulo (m)");
            Sensibilidade = c.CreateEntry("Sensibilidade", 2f, description: "Graus por unidade de Mouse X/Y");
            InverterY = c.CreateEntry("InverterY", false, description: "Inverte o eixo vertical do mouse");
            CampoDeVisao = c.CreateEntry("CampoDeVisao", 70f, description: "Campo de visão vertical a pé (graus). 0 = o da câmera do jogo");
            Balanco = c.CreateEntry("Balanco", 1f, description: "Balanço da cabeça ao andar (0 = desligado)");
            DistanciaParaEntrar = c.CreateEntry("DistanciaParaEntrar", 2.5f, description: "Só em ônibus sem caminhos de passageiros (não dá para andar dentro): distância máxima de uma porta para G levar ao banco (m)");
            VelocidadeMaximaParaDescer = c.CreateEntry("VelocidadeMaximaParaDescer", 2f, description: "Só desce com o ônibus abaixo desta velocidade (km/h)");
        }
    }
}
