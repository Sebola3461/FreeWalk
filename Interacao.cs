using PBSLoaderCommonUtils;
using UnityEngine;

namespace AndarAPe
{
    // Aviso preso na porta do ônibus: olhando para uma porta de perto (por dentro ou por fora), aparece
    // "[E] Abrir porta frontal" sobre o vão dela, e a tecla abre ou fecha aquela porta pelo comando do jogo
    // (o mesmo do painel: sem ar, a PBSLoaderCommonUtils bloqueia e avisa).
    internal static class Interacao
    {
        const float Alcance = 2.4f;        // dos olhos ao centro do vão
        const float AnguloMaximo = 50f;    // entre o olhar e a porta

        static Interior.PortaDentro _alvo;
        static Vector3 _ancora;            // mundo
        internal static Interior.PortaDentro Alvo => _alvo;
        internal static string Texto => _alvo == null ? "" : (_alvo.Porta.Aberta ? "Fechar " : "Abrir ") + _alvo.Nome;
        internal static bool InteragirTeste;

        internal static void Limpar() => _alvo = null;

        internal static void Quadro(Transform cam, bool podeAgir)
        {
            _alvo = null;
            var raiz = Veiculo.Raiz;
            if (cam == null || raiz == null) return;
            float melhor = float.MaxValue;
            foreach (var d in Interior.Portas)
            {
                var c = raiz.TransformPoint(Interior.CentroDaPorta(d));
                var v = c - cam.position;
                float dist = v.magnitude;
                if (dist > Alcance) continue;
                // Só o ângulo no plano: a porta é alta, olhar para cima ou para baixo não tira o aviso.
                float ang = Vector3.Angle(Vector3.ProjectOnPlane(cam.forward, raiz.up), Vector3.ProjectOnPlane(v, raiz.up));
                if (ang > AnguloMaximo && dist > 0.9f) continue;
                float nota = dist + ang / 45f;
                if (nota < melhor) { melhor = nota; _alvo = d; _ancora = c; }
            }
            if (_alvo == null) return;
            // Aceita modificadores a mais: agachado (Ctrl) ou correndo (Shift), E ainda abre a porta.
            var comb = Cfg.Interagir.Combinacao;
            bool apertou = InteragirTeste || (podeAgir && !comb.Vazia && Input.GetKeyDown(comb.Tecla) && Cfg.Interagir.Segurando);
            InteragirTeste = false;
            if (apertou)
            {
                Log.Msg($"{Texto} ({_alvo.Porta})");
                Comandos.Porta(_alvo.Porta);
            }
        }

        // ---------- desenho ----------

        static Texture2D _fundo, _tecla, _faixa;
        static GUIStyle _estTecla, _estTexto;

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static GUIStyle Estilo(int tam, Color cor, FontStyle f)
        {
            var s = new GUIStyle();
            s.fontSize = tam; s.fontStyle = f; s.alignment = TextAnchor.MiddleCenter;
            s.normal.textColor = cor;
            s.wordWrap = false; s.clipping = TextClipping.Overflow;
            return s;
        }

        internal static void Desenhar()
        {
            if (_alvo == null || !Pedestre.Andando || Event.current == null || Event.current.type != EventType.Repaint) return;
            var cam = Camera.main;
            if (cam == null) return;
            var sp = cam.WorldToScreenPoint(_ancora);
            if (sp.z <= 0.05f) return;

            if (_fundo == null)
            {
                _fundo = Tex(new Color(0.07f, 0.07f, 0.08f, 0.88f));
                _tecla = Tex(Color.white);
                _faixa = Tex(new Color(0.84f, 0f, 0.18f, 1f));   // vermelho da PBSLoaderCommonInterface
                _estTecla = Estilo(18, Color.black, FontStyle.Bold);
                _estTexto = Estilo(18, Color.white, FontStyle.Bold);
            }
            float k = Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
            _estTecla.fontSize = Mathf.RoundToInt(18 * k);
            _estTexto.fontSize = Mathf.RoundToInt(18 * k);

            string tecla = Cfg.Interagir.Combinacao.ToString();
            string texto = Texto.ToUpperInvariant();
            var tamTexto = _estTexto.CalcSize(new GUIContent(texto));
            float h = 40f * k, pad = 12f * k, larTecla = Mathf.Max(h - 12f * k, _estTecla.CalcSize(new GUIContent(tecla)).x + 12f * k);
            float lar = 4f * k + pad + larTecla + pad + tamTexto.x + pad;
            float x = sp.x - lar / 2f, y = Screen.height - sp.y - h / 2f;

            GUI.DrawTexture(new Rect(x, y, lar, h), _fundo);
            GUI.DrawTexture(new Rect(x, y, 4f * k, h), _faixa);
            var rTecla = new Rect(x + 4f * k + pad, y + 6f * k, larTecla, h - 12f * k);
            GUI.DrawTexture(rTecla, _tecla);
            GUI.Label(rTecla, tecla, _estTecla);
            GUI.Label(new Rect(rTecla.xMax + pad, y, tamTexto.x, h), texto, _estTexto);
        }
    }
}
