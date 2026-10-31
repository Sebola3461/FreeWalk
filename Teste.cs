using System;
using System.IO;
using System.Linq;
using System.Text;
using Il2Cpp;
using MelonLoader.Utils;
using PBSLoaderCommonUtils;
using UnityEngine;

namespace AndarAPe
{
    // --andarape-teste (junto com --pbsloadercommoninterface-partida): espera o ônibus assentar e, da cabine, sai do banco para o
    // salão, anda até a porta frontal fechada (tem que barrar), abre pelo aviso, desce pela porta, volta por ela,
    // anda até o banco e volta a dirigir. Grava UserData/AndarAPe/teste.txt e capturas teste_*.png.
    // --andarape-pontos só grava os caminhos dos passageiros e as portas. --andarape-sair fecha o jogo no fim.
    internal static class Teste
    {
        static readonly bool Ativo = Environment.GetCommandLineArgs().Contains("--andarape-teste");
        static readonly bool SoPontos = Environment.GetCommandLineArgs().Contains("--andarape-pontos");
        static readonly bool Sair = Environment.GetCommandLineArgs().Contains("--andarape-sair");
        static readonly string Pasta = Path.Combine(MelonEnvironment.UserDataDirectory, "AndarAPe");
        static readonly StringBuilder _sb = new();
        static int _fase, _geracao, _ok, _total;
        static float _t;
        static Vector3 _p0;
        static Interior.PortaDentro _frente;

        static void Linha(string s) { _sb.AppendLine(s); Log.Msg("Teste: " + s); }

        static void Checar(bool ok, string oque)
        {
            _total++; if (ok) _ok++;
            Linha((ok ? "ok    " : "FALHA ") + oque);
        }

        static void Foto(string nome)
        {
            Directory.CreateDirectory(Pasta);
            ScreenCapture.CaptureScreenshot(Path.Combine(Pasta, "teste_" + nome + ".png"));
        }

        static Vector3 CentroFrente => Veiculo.Raiz.TransformPoint(Interior.CentroDaPorta(_frente));

        internal static void Quadro()
        {
            if (!Ativo || _fase < 0) return;
            try { Fase(); }
            catch (Exception e) { Linha($"erro na fase {_fase}: {e}"); Fim(); }
        }

        static void Fase()
        {
            float agora = Time.unscaledTime;
            float dt = agora - _t;
            var c = Interior.Caixa;
            switch (_fase)
            {
                case 0:   // ônibus carregado e estável (o jogo troca o MEPControlesBus durante a carga)
                    if (!Veiculo.Existe || MepMobileControls.instance == null || MouseOrbitCSHARP.instance == null) { _t = agora; return; }
                    if (Veiculo.Geracao != _geracao) { _geracao = Veiculo.Geracao; _t = agora; return; }
                    if (dt < 10f) return;
                    Linha("AndarAPe teste " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - " + Veiculo.Nome);
                    Linha("portas: " + string.Join(", ", Veiculo.Portas.Todas) + ", ar " + Ar.Pressao.ToString("0.0") + " bar");
                    try { Pontos(); } catch (Exception e) { Linha("pontos: " + e.Message); }
                    if (SoPontos) { Fim(); return; }
                    _frente = Interior.Portas.FirstOrDefault(d => d.Porta.Lado == Lado.Direito && d.Porta.Numero == 1) ?? Interior.Portas.FirstOrDefault();
                    if (_frente == null) { Linha("sem portas"); Fim(); return; }
                    if (_frente.Porta.Aberta) Comandos.Porta(_frente.Porta);   // começa fechada
                    MepMobileControls.instance.SetCameraMotorista();
                    Proxima(agora);
                    break;
                case 1:
                    if (dt < 2.5f) return;
                    Linha("na cabine: " + Pedestre.Estado());
                    Foto("0_cabine");
                    Checar(!_frente.Porta.Aberta, "porta frontal fechada no começo");
                    Checar(Pedestre.Descer(), "saiu do banco (" + Pedestre.UltimoMotivo + ")");
                    if (!Pedestre.Andando) { Fim(); return; }
                    Proxima(agora);
                    break;
                case 2:
                    if (dt < 1.5f) return;
                    Linha("no salão: " + Pedestre.Estado());
                    Checar(Pedestre.Dentro, "começou dentro do ônibus");
                    Checar(Mathf.Abs(Pedestre.Local.y - Interior.PisoSalao) < 0.05f, $"no piso do salão (y {Pedestre.Local.y:0.00}, salão {Interior.PisoSalao:0.00})");
                    var d0 = Pedestre.Local - Pedestre.LocalMotorista;
                    Checar(new Vector2(d0.x, d0.z).magnitude < 2f, $"perto do banco do motorista ({new Vector2(d0.x, d0.z).magnitude:0.00} m)");
                    Checar(Pedestre.DentroDaCarroceria(MouseOrbitCSHARP.instance.transform.position), "câmera dentro da carroceria");
                    Checar(MouseOrbitCSHARP.tahNaCameraInterna, "jogo sabe que a câmera está dentro");
                    Checar(Teclado.JogoSuspenso, "controles do jogo suspensos");
                    Checar(!mmcFaixa(), "faixa da câmera livre escondida");
                    Faixas();
                    Foto("1_salao");
                    Pedestre.OlharPara(CentroFrente);
                    Pedestre.EntradaTeste = new Vector2(0f, 1f);
                    Proxima(agora);
                    break;
                case 3:   // até a porta fechada; continua empurrando
                    if (dt < 4f) return;
                    Pedestre.EntradaTeste = Vector2.zero;
                    float x = Pedestre.Local.x;
                    Linha("na porta fechada: " + Pedestre.Estado());
                    Checar(Pedestre.Dentro && x < c.max.x - 0.2f, $"porta fechada barrou (x {x:0.00}, parede {c.max.x:0.00})");
                    Checar(Interacao.Alvo == _frente && Interacao.Texto.StartsWith("Abrir"), $"aviso na porta: '{Interacao.Texto}'");
                    Checar(Interior.Dentro(MouseOrbitCSHARP.instance.transform.position), "câmera ainda dentro do ônibus na porta");
                    Checar(!mmcFaixa(), "faixa da câmera livre continua escondida");
                    Foto("2_aviso");
                    Interacao.InteragirTeste = true;
                    Proxima(agora);
                    break;
                case 4:
                    if (dt < 3f) return;
                    Checar(_frente.Porta.Aberta, $"porta frontal abriu (ar {Ar.Pressao:0.0} bar)");
                    Checar(Interacao.Texto.StartsWith("Fechar"), $"aviso virou '{Interacao.Texto}'");
                    Foto("3_aberta");
                    _p0 = Pedestre.Pe;
                    Pedestre.OlharPara(CentroFrente);
                    Pedestre.EntradaTeste = new Vector2(0f, 1f);
                    Proxima(agora);
                    break;
                case 5:   // desce pelos degraus até sair
                    if (Pedestre.Dentro && dt < 5f) return;
                    Checar(!Pedestre.Dentro, $"desceu pela porta ({dt:0.0} s)");
                    Linha("passou a porta: " + Pedestre.Estado());
                    Proxima(agora);
                    break;
                case 6:   // um pouco longe, e vira para a porta
                    if (dt < 1.5f) return;
                    Pedestre.EntradaTeste = Vector2.zero;
                    Checar(Pedestre.Corpo.enabled && Pedestre.Corpo.isGrounded, "no chão do lado de fora");
                    Checar(!Pedestre.DentroDaCarroceria(Pedestre.Pe + Vector3.up), "fora da carroceria");
                    Checar(!MouseOrbitCSHARP.tahNaCameraInterna, "jogo sabe que a câmera está fora");
                    Linha($"desceu {_p0.y - Pedestre.Pe.y:0.00} m do salão até a calçada");
                    Pedestre.OlharPara(CentroFrente);
                    Checar(!Pedestre.Subir(false) && Pedestre.Andando, "de fora, G não leva ao banco");
                    Pedestre.AgacharTeste = true;
                    Proxima(agora);
                    break;
                case 7:   // agacha e levanta do lado de fora
                    if (dt < 0.6f) return;
                    Checar(Pedestre.Agachado && Pedestre.Corpo.height < 1.2f, $"agachou (cápsula {Pedestre.Corpo.height:0.00} m)");
                    Pedestre.AgacharTeste = false;
                    Foto("4_fora");
                    Proxima(agora);
                    break;
                case 8:
                    if (dt < 0.6f) return;
                    Checar(!Pedestre.Agachado && Pedestre.Corpo.height > 1.6f, $"levantou (cápsula {Pedestre.Corpo.height:0.00} m)");
                    Pedestre.EntradaTeste = new Vector2(0f, 1f);
                    Proxima(agora);
                    break;
                case 9:   // volta pela porta
                    if (!Pedestre.Dentro && dt < 6f) return;
                    Checar(Pedestre.Dentro, $"entrou pela porta ({dt:0.0} s)");
                    Proxima(agora);
                    break;
                case 10:
                    if (dt < 1.5f) return;
                    Pedestre.EntradaTeste = Vector2.zero;
                    Linha("de volta: " + Pedestre.Estado());
                    Checar(Pedestre.Local.y > Interior.PisoSalao - 0.4f, $"subiu os degraus (y {Pedestre.Local.y:0.00})");
                    Pedestre.OlharPara(Veiculo.Raiz.TransformPoint(Pedestre.LocalMotorista));
                    Pedestre.EntradaTeste = new Vector2(0f, 1f);
                    Proxima(agora);
                    break;
                case 11:  // até o banco
                    if (!Pedestre.PertoDoBanco() && dt < 6f) return;
                    Pedestre.EntradaTeste = Vector2.zero;
                    Checar(Pedestre.PertoDoBanco(), $"chegou ao banco ({dt:0.0} s)");
                    Proxima(agora);
                    break;
                case 12:
                    if (dt < 0.6f) return;
                    Checar(Avisos.Ativos.Any(a => a.Origem == "AndarAPe" && a.Id == "entrar"), "dica de dirigir na tela");
                    Foto("5_banco");
                    Checar(Pedestre.Subir(false), "voltou a dirigir");
                    Proxima(agora);
                    break;
                case 13:
                    if (dt < 2f) return;
                    Linha("depois: " + Pedestre.Estado());
                    Checar(!Pedestre.Andando, "não está mais a pé");
                    Checar(MouseOrbitCSHARP.MEP_SelCamera == MouseOrbitCSHARP.ZipoCamera.camInterna, "câmera de volta na cabine");
                    Checar(!Teclado.JogoSuspenso, "controles do jogo devolvidos");
                    Foto("6_voltou");
                    if (_frente.Porta.Aberta) Comandos.Porta(_frente.Porta);
                    Proxima(agora);
                    break;
                case 14:
                    if (dt < 1f) return;
                    Fim();
                    break;
            }
        }

        // Onde está o texto "WASD" da faixa da câmera livre.
        static void Faixas()
        {
            var m = MepMobileControls.instance;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>())
            {
                if (t.text == null || !t.text.Contains("WASD")) continue;
                string cam = ""; for (var x = t.transform; x != null; x = x.parent) cam = x.name + (x.gameObject.activeSelf ? "" : "(off)") + "/" + cam;
                Linha($"   faixa: {cam} ativo {t.gameObject.activeInHierarchy} é txtCanvasStatus {t == m.txtCanvasStatus}");
            }
            if (m.fundoCanvasStatus != null) Linha($"   fundoCanvasStatus {m.fundoCanvasStatus.name} ativo {m.fundoCanvasStatus.activeSelf}");
        }

        static bool mmcFaixa() => UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()
            .Any(t => t.text != null && t.text.Contains("WASD") && t.gameObject.activeInHierarchy && !t.transform.root.name.StartsWith("PBSLoaderCommonInterface"));

        static void Proxima(float agora) { _fase++; _t = agora; }

        // Pontos de caminho dos passageiros e portas, em coordenadas do ônibus.
        static void Pontos()
        {
            var raiz = Veiculo.Raiz;
            var re = new System.Text.RegularExpressions.Regex(@"^_[rl]\d+[a-z]\d+_", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            int n = 0;
            foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
                if (re.IsMatch(t.name) && n++ < 200)
                    Linha($"   ponto {t.name,-28} {Pedestre.Fmt(raiz.InverseTransformPoint(t.position))}");
            var c = Interior.Caixa;
            Linha($"caixa {Pedestre.Fmt(c.center)} tamanho {Pedestre.Fmt(c.size)}, salão y {Interior.PisoSalao:0.00}");
            foreach (var d in Interior.Portas)
                Linha($"   {d.Nome} {d.Porta} z {d.Z0:0.00}..{d.Z1:0.00}, centro {Pedestre.Fmt(Interior.CentroDaPorta(d))}");
        }

        static void Fim()
        {
            _fase = -1;
            Pedestre.EntradaTeste = Vector2.zero;
            Pedestre.AgacharTeste = false;
            Linha($"resultado: {_ok}/{_total} ok");
            Directory.CreateDirectory(Pasta);
            File.WriteAllText(Path.Combine(Pasta, "teste.txt"), _sb.ToString(), new UTF8Encoding(false));
            Log.Msg("Teste gravado em UserData/AndarAPe/teste.txt" + (Sair ? ", fechando o jogo" : ""));
            if (Sair) Application.Quit();
        }
    }
}
