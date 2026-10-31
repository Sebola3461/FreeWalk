using System.Collections.Generic;
using System.Text.RegularExpressions;
using PBSLoaderCommonUtils;
using UnityEngine;

namespace AndarAPe
{
    // O ônibus visto de dentro, em coordenadas do ônibus (Veiculo.Raiz). Não há colisor de piso nem de bancos:
    // o piso vem dos caminhos que os passageiros do jogo seguem. São objetos vazios do modelo com nome
    // "_<lado><porta><caminho><ordem>_", como "_r2y11_" (porta R2, caminho y, 11º ponto), que vão da calçada,
    // pelos degraus, até o corredor. A altura dos pontos é a dos pés dos passageiros.
    // No 1519: calçada y 0,25, degrau 0,60, salão 0,89, corredor em x -0,15; parede direita em x 1,32.
    internal static class Interior
    {
        internal sealed class PortaDentro
        {
            internal Porta Porta;
            internal float Lado;        // +1 direita (+x), -1 esquerda
            internal float Z0, Z1;      // faixa livre da porta ao longo do ônibus
            internal float Zc => (Z0 + Z1) / 2f;
            internal string Nome;       // "porta frontal", "porta do meio", ...
        }

        static readonly Regex NomePonto = new(@"^_([rl])(\d+)([a-z])(\d+)_", RegexOptions.IgnoreCase);

        static Transform _de;
        static int _geracao = -1, _nPortas = -1;
        static Bounds _caixa;
        static readonly List<(Vector3 a, Vector3 b)> _segmentos = new();
        static readonly List<Vector3> _pontosSalao = new();
        static readonly List<PortaDentro> _portas = new();
        static float _pisoSalao;

        internal static Bounds Caixa { get { Atualizar(); return _caixa; } }
        internal static bool TemCaminhos { get { Atualizar(); return _segmentos.Count > 0; } }
        internal static IReadOnlyList<PortaDentro> Portas { get { Atualizar(); return _portas; } }
        internal static float PisoSalao { get { Atualizar(); return _pisoSalao; } }

        /// <summary>Lado de fora da parede: x local da parede do lado da porta.</summary>
        internal static float Parede(float lado) => lado > 0f ? _caixa.max.x : _caixa.min.x;

        internal static void Atualizar()
        {
            var raiz = Veiculo.Raiz;
            if (raiz == _de && Veiculo.Geracao == _geracao && Veiculo.Portas.Todas.Count == _nPortas) return;
            _de = raiz; _geracao = Veiculo.Geracao; _nPortas = Veiculo.Portas.Todas.Count;
            _segmentos.Clear(); _pontosSalao.Clear(); _portas.Clear();
            if (raiz == null) return;
            _caixa = CaixaDosColisores(raiz, out bool temColisor);

            // Caminhos: agrupa por porta + letra e ordena pelo número.
            var grupos = new Dictionary<string, SortedList<int, Vector3>>();
            var pontosDaPorta = new Dictionary<string, List<Vector3>>();
            foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
            {
                var m = NomePonto.Match(t.name);
                if (!m.Success) continue;
                string porta = m.Groups[1].Value.ToLowerInvariant() + m.Groups[2].Value;
                string chave = porta + m.Groups[3].Value.ToLowerInvariant();
                int ordem = int.Parse(m.Groups[4].Value);
                var l = raiz.InverseTransformPoint(t.position);
                if (!grupos.TryGetValue(chave, out var g)) grupos[chave] = g = new SortedList<int, Vector3>();
                g[ordem] = l;
                if (!pontosDaPorta.TryGetValue(porta, out var pp)) pontosDaPorta[porta] = pp = new List<Vector3>();
                pp.Add(l);
            }
            float ySalao = float.MinValue;
            foreach (var g in grupos.Values)
            {
                Vector3? ant = null;
                foreach (var p in g.Values)
                {
                    if (ant.HasValue)
                    {
                        var d = p - ant.Value; d.y = 0f;
                        // Trechos verticais (degrau) não têm extensão no chão: o piso vem dos vizinhos.
                        if (d.magnitude > 0.02f) _segmentos.Add((ant.Value, p));
                    }
                    ant = p;
                    ySalao = Mathf.Max(ySalao, p.y);
                }
            }
            _pisoSalao = ySalao > float.MinValue ? ySalao : _caixa.min.y + 0.7f;
            foreach (var g in grupos.Values)
                foreach (var p in g.Values)
                    if (p.y > _pisoSalao - 0.08f && DentroXZ(p, 0.05f)) _pontosSalao.Add(p);

            // Portas: a faixa em z é a dos pontos de caminho junto à parede (degraus), com folga para a folha.
            int maxDir = 0, maxEsq = 0;
            foreach (var p in Veiculo.Portas.Todas)
                if (p.Lado == Lado.Direito) maxDir = Mathf.Max(maxDir, p.Numero); else maxEsq = Mathf.Max(maxEsq, p.Numero);
            foreach (var p in Veiculo.Portas.Todas)
            {
                float lado = p.Lado == Lado.Direito ? 1f : -1f;
                var pd = new PortaDentro { Porta = p, Lado = lado };
                string chave = (p.Lado == Lado.Direito ? "r" : "l") + p.Numero;
                float z0 = float.MaxValue, z1 = float.MinValue;
                if (pontosDaPorta.TryGetValue(chave, out var pts))
                    foreach (var q in pts)
                        if (lado * (q.x - _caixa.center.x) > _caixa.extents.x - 0.8f) { z0 = Mathf.Min(z0, q.z); z1 = Mathf.Max(z1, q.z); }
                if (z0 > z1 && p.Jogo != null) z0 = z1 = raiz.InverseTransformPoint(p.Jogo.transform.position).z;
                if (z0 > z1) continue;
                pd.Z0 = z0 - 0.42f; pd.Z1 = z1 + 0.42f;
                int max = p.Lado == Lado.Direito ? maxDir : maxEsq;
                pd.Nome = p.Numero == 1 ? "porta frontal" : p.Numero == max ? "porta traseira" : max == 3 && p.Numero == 2 ? "porta do meio" : "porta " + p.Numero;
                _portas.Add(pd);
            }
            Log.Msg($"interior de {raiz.name}: caixa {Pedestre.Fmt(_caixa.center)} tamanho {Pedestre.Fmt(_caixa.size)}{(temColisor ? "" : " (sem colisor)")}, " +
                    $"{grupos.Count} caminhos, {_segmentos.Count} trechos, salão y {_pisoSalao:0.00}, portas: " +
                    string.Join(", ", _portas.ConvertAll(d => $"{d.Porta.Lado}{d.Porta.Numero} z {d.Z0:0.00}..{d.Z1:0.00}")));
        }

        // A caixa pelo colisor sólido da carroceria (o mesmo que bate em paredes). A Veiculo.Caixa vem dos
        // renderers e pega peças soltas que a deixam bem maior que o ônibus.
        static Bounds CaixaDosColisores(Transform raiz, out bool tem)
        {
            tem = false; var b = new Bounds();
            foreach (var c in raiz.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                Bounds lb;
                var mc = c.TryCast<MeshCollider>();
                var bc = c.TryCast<BoxCollider>();
                if (mc != null && mc.sharedMesh != null) lb = mc.sharedMesh.bounds;
                else if (bc != null) lb = new Bounds(bc.center, bc.size);
                else continue;   // rodas, esferas dos botões
                if (lb.size.magnitude < 1f) continue;
                for (int i = 0; i < 8; i++)
                {
                    var canto = new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z);
                    var l = raiz.InverseTransformPoint(c.transform.TransformPoint(canto));
                    if (!tem) { b = new Bounds(l, Vector3.zero); tem = true; } else b.Encapsulate(l);
                }
            }
            if (!tem) b = Veiculo.Caixa;
            return b;
        }

        internal static bool DentroXZ(Vector3 l, float margem) =>
            Mathf.Abs(l.x - _caixa.center.x) < _caixa.extents.x - margem && Mathf.Abs(l.z - _caixa.center.z) < _caixa.extents.z - margem;

        internal static bool Dentro(Vector3 mundo)
        {
            Atualizar();
            var raiz = Veiculo.Raiz;
            if (raiz == null) return false;
            var p = raiz.InverseTransformPoint(mundo);
            return DentroXZ(p, 0f) && p.y > _caixa.min.y - 0.5f && p.y < _caixa.max.y;
        }

        /// <summary>Altura do piso (local) num ponto do ônibus: o trecho de caminho mais próximo no plano.</summary>
        internal static float Piso(Vector3 l)
        {
            Atualizar();
            if (_segmentos.Count == 0) return _pisoSalao;
            float melhor = float.MaxValue, y = _pisoSalao;
            var p = new Vector2(l.x, l.z);
            foreach (var (a, b) in _segmentos)
            {
                var a2 = new Vector2(a.x, a.z); var ab = new Vector2(b.x, b.z) - a2;
                float t = Mathf.Clamp01(Vector2.Dot(p - a2, ab) / ab.sqrMagnitude);
                float d = (a2 + ab * t - p).sqrMagnitude;
                if (d < melhor) { melhor = d; y = Mathf.Lerp(a.y, b.y, t); }
            }
            return y;
        }

        /// <summary>
        /// Prende um ponto (local) ao corredor dos passageiros: no máximo <paramref name="raio"/> do caminho mais
        /// perto, no plano. É por onde os passageiros andam (corredor e escadas das portas); fora dele há bancos,
        /// painel e para-brisa, sem colisão.
        /// </summary>
        internal static Vector3 NoCorredor(Vector3 l, float raio)
        {
            Atualizar();
            if (_segmentos.Count == 0) return l;
            float melhor = float.MaxValue; Vector2 perto = default;
            var p = new Vector2(l.x, l.z);
            foreach (var (a, b) in _segmentos)
            {
                var a2 = new Vector2(a.x, a.z); var ab = new Vector2(b.x, b.z) - a2;
                var q = a2 + ab * Mathf.Clamp01(Vector2.Dot(p - a2, ab) / ab.sqrMagnitude);
                float d = (q - p).sqrMagnitude;
                if (d < melhor) { melhor = d; perto = q; }
            }
            if (melhor <= raio * raio) return l;
            var r = perto + (p - perto).normalized * raio;
            return new Vector3(r.x, l.y, r.y);
        }

        /// <summary>Ponto do salão mais perto de um ponto local (para nascer ao sair do banco do motorista).</summary>
        internal static bool PontoMaisProximo(Vector3 l, out Vector3 ponto)
        {
            Atualizar();
            ponto = default;
            float melhor = float.MaxValue;
            foreach (var p in _pontosSalao)
            {
                float d = new Vector2(p.x - l.x, p.z - l.z).sqrMagnitude;
                if (d < melhor) { melhor = d; ponto = p; }
            }
            return melhor < float.MaxValue;
        }

        /// <summary>A porta aberta cuja faixa contém z, do lado indicado (0 = qualquer).</summary>
        internal static PortaDentro PortaAbertaEm(float z, float lado = 0f)
        {
            foreach (var d in Portas)
                if (d.Porta.Aberta && (lado == 0f || d.Lado == lado) && z >= d.Z0 && z <= d.Z1) return d;
            return null;
        }

        /// <summary>Centro do vão da porta (local), um pouco abaixo dos olhos de quem está no salão: onde fica o aviso.</summary>
        internal static Vector3 CentroDaPorta(PortaDentro d) => new(Parede(d.Lado), PisoSalao + 0.9f, d.Zc);
    }
}
