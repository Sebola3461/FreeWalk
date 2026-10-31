using System.Globalization;
using System.Text;
using Il2Cpp;
using PBSLoaderCommonUtils;
using UnityEngine;

namespace AndarAPe
{
    // O jogador a pé, com a câmera do jogo nos olhos. Dois modos:
    //   fora   um CharacterController no mundo (chão e paredes do mapa, a carroceria do ônibus bloqueia);
    //   dentro coordenadas do ônibus: piso pelos caminhos dos passageiros (Interior), paredes pela caixa da
    //          carroceria, e o jogador vai junto se o ônibus andar.
    // A passagem entre os dois só acontece pela faixa de uma porta aberta.
    internal static class Pedestre
    {
        const string Dono = "AndarAPe";
        const float Raio = 0.3f, AlturaEmPe = 1.7f, AlturaAgachado = 1.1f, G = 9.81f;
        const float QuedaMaxima = 25f;
        const float LarguraCorredor = 0.45f;   // dentro: distância máxima do caminho dos passageiros   // caiu isso abaixo do último chão: o mapa não tem colisor ali

        internal static bool Andando { get; private set; }

        static GameObject _corpo;
        static CharacterController _cc;
        static Transform _cam;
        static Camera _camera;
        static Transform _paiCamera;
        static float _fovOriginal, _nearOriginal;
        static int _geracao;
        static float _yaw, _pitch, _olhos;
        static Vector3 _vel;
        static bool _pularAntes, _agachado, _pausadoAntes;
        static Vector3 _ultimoChao;
        // Dentro do ônibus (coordenadas de Veiculo.Raiz).
        static bool _dentro;
        static Vector3 _local, _velLocal, _localMotorista;
        static float _yawLocal, _altura, _vy;
        static float _passo;
        static float _proximaDica;

        // Teste automático: movimento (x = lado, y = frente) e pulo sem teclado.
        internal static Vector2 EntradaTeste;
        internal static bool PularTeste;
        internal static bool CorrerTeste;
        internal static bool AgacharTeste;
        internal static CharacterController Corpo => _cc;
        internal static bool Dentro => _dentro;
        internal static bool Agachado => _agachado;
        internal static Vector3 Local => _local;
        internal static Vector3 LocalMotorista => _localMotorista;
        internal static Vector3 Pe => _dentro && Veiculo.Raiz != null ? Veiculo.Raiz.TransformPoint(_local) : _corpo != null ? _corpo.transform.position : Vector3.zero;

        // Gira o olhar (só no plano) para um ponto do mundo.
        internal static void OlharPara(Vector3 mundo)
        {
            var raiz = Veiculo.Raiz;
            var de = _cam != null ? _cam.position : Pe;
            if (_dentro && raiz != null)
            {
                var d = raiz.InverseTransformDirection(mundo - de);
                _yawLocal = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            else
            {
                var d = mundo - de;
                _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            _pitch = 0f;
        }

        static float YawOnibus()
        {
            var f = Vector3.ProjectOnPlane(Veiculo.Raiz.forward, Vector3.up);
            return f.sqrMagnitude < 1e-4f ? 0f : Quaternion.LookRotation(f).eulerAngles.y;
        }
        internal static string UltimoMotivo = "";

        static string Controles()
        {
            static string T(Atalho a) => a.Combinacao.ToString()
                .Replace("LeftShift", "Shift").Replace("LeftControl", "Ctrl").Replace("Space", "Espaço");
            return $"{T(Cfg.Frente)}{T(Cfg.Esquerda)}{T(Cfg.Tras)}{T(Cfg.Direita)} andar, {T(Cfg.Correr)} correr, "
                 + $"{T(Cfg.Pular)} pular, {T(Cfg.Agachar)} agachar";
        }

        internal static void Alternar()
        {
            if (Andando) Subir(false);
            else Descer();
        }

        // ---------- descer ----------

        internal static bool Descer()
        {
            if (Andando) return true;
            var mmc = MepMobileControls.instance;
            var orbit = MouseOrbitCSHARP.instance;
            if (!Veiculo.Existe || Veiculo.Raiz == null || mmc == null || orbit == null)
                return Recusar("Sem ônibus para descer");
            if (MepMobileControls.taPausado) return Recusar("Jogo pausado");
            if (Veiculo.VelocidadeKmh > Cfg.VelocidadeMaximaParaDescer.Value)
                return Recusar("Pare o ônibus para descer");

            var cam = orbit.transform;
            var raiz = Veiculo.Raiz;
            Vector3 olhosDentro = cam.position;
            _localMotorista = raiz.InverseTransformPoint(olhosDentro);
            // Do banco do motorista para o ponto de caminho dos passageiros mais próximo, no salão. Ônibus sem
            // caminhos (o jogador não conseguiria andar dentro): direto para fora, ao lado da porta.
            bool dentro = Interior.TemCaminhos && Interior.PontoMaisProximo(_localMotorista, out _local);
            Vector3 pe = default, olhar = default;
            if (dentro) pe = raiz.TransformPoint(_local);
            else if (!PontoDeSaida(olhosDentro, out pe, out olhar))
                return Recusar("Sem espaço para descer aqui");

            // Câmera livre do jogo: som e carroceria de fora, freio de mão puxado. A faixa verde dela ("WASD: mover,
            // Shift+clique: reposicionar o veículo") não vale a pé: some (EsconderFaixa) e os controles a pé vão nos
            // avisos. O motorista do jogo, que a câmera livre mostra no banco, some enquanto você anda.
            // (MepMobileControls.MostraStatus é um corpo vazio compartilhado, RVA 0x2DF6A0: não patchear.)
            mmc.ClicouFreeCameraLivre(true);
            mmc.MostraMotorista(false);
            _faixa = null;
            _procurarFaixaAte = Time.unscaledTime + 3f;
            Avisos.Notificar(Dono, "controles", Gravidade.Info, "A pé", Controles(), 10f);

            _cam = cam;
            _camera = cam.GetComponent<Camera>();
            if (_camera == null) _camera = Camera.main;
            _paiCamera = cam.parent;
            cam.SetParent(null, true);
            if (_camera != null)
            {
                _fovOriginal = _camera.fieldOfView;
                _nearOriginal = _camera.nearClipPlane;
                if (Cfg.CampoDeVisao.Value > 1f) _camera.fieldOfView = Cfg.CampoDeVisao.Value;
                _camera.nearClipPlane = Mathf.Min(_nearOriginal, 0.05f);
            }

            CriarCorpo(pe);
            _dentro = dentro;
            _cc.enabled = !dentro;
            _velLocal = Vector3.zero; _altura = 0f; _vy = 0f;
            if (dentro)
            {
                // Olhando para a porta mais perto.
                _yawLocal = 0f;
                float melhor = float.MaxValue;
                foreach (var d in Interior.Portas)
                {
                    var c = Interior.CentroDaPorta(d) - _local;
                    float dist = new Vector2(c.x, c.z).magnitude;
                    if (dist < melhor) { melhor = dist; _yawLocal = Mathf.Atan2(c.x, c.z) * Mathf.Rad2Deg; }
                }
            }
            else _yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(olhar, Vector3.up)).eulerAngles.y;
            _pitch = 0f;
            _vel = Vector3.zero;
            _agachado = false;
            _olhos = Cfg.AlturaOlhos.Value;
            _ultimoChao = pe;
            _geracao = Veiculo.Geracao;
            _pausadoAntes = false;
            _pularAntes = true;   // não pula com o Space que talvez esteja apertado
            Andando = true;
            PosicionarCamera();
            Log.Msg($"saiu do banco para {(dentro ? "o salão em " + Fmt(_local) : "fora em " + Fmt(pe))} (câmera {MouseOrbitCSHARP.MEP_SelCamera}, pai era {(_paiCamera != null ? _paiCamera.name : "nenhum")})");
            return true;
        }

        static bool Recusar(string motivo)
        {
            UltimoMotivo = motivo;
            Avisos.Notificar(Dono, "recusado", Gravidade.Aviso, motivo, null, 3f);
            return false;
        }

        // Saída pela porta da frente (direita, menor número), senão qualquer porta, senão ao lado do motorista.
        // O pé fica fora da caixa do ônibus, na altura do chão achado por raio, num lugar onde a cápsula cabe.
        static bool PontoDeSaida(Vector3 olhosDentro, out Vector3 pe, out Vector3 olhar)
        {
            var raiz = Veiculo.Raiz;
            var caixa = Carroceria();
            var zs = new System.Collections.Generic.List<(float z, float lado)>();
            Porta melhor = null;
            foreach (var p in Veiculo.Portas.Todas)
            {
                if (p.Jogo == null) continue;
                if (melhor == null || (p.Lado == Lado.Direito && melhor.Lado != Lado.Direito)
                    || (p.Lado == melhor.Lado && p.Numero < melhor.Numero)) melhor = p;
            }
            if (melhor != null) zs.Add((raiz.InverseTransformPoint(melhor.Jogo.transform.position).z, melhor.Lado == Lado.Direito ? 1f : -1f));
            foreach (var p in Veiculo.Portas.Todas)
                if (p != melhor && p.Jogo != null)
                    zs.Add((raiz.InverseTransformPoint(p.Jogo.transform.position).z, p.Lado == Lado.Direito ? 1f : -1f));
            float zMot = raiz.InverseTransformPoint(olhosDentro).z;
            zs.Add((zMot, -1f));
            zs.Add((zMot, 1f));

            foreach (var (z0, lado) in zs)
                for (int i = 0; i < 9; i++)
                {
                    float dz = (i % 2 == 1 ? 1 : -1) * ((i + 1) / 2) * 0.6f;
                    for (float folga = 0.5f; folga <= 1.7f; folga += 0.6f)
                    {
                        var local = new Vector3(caixa.center.x + lado * (caixa.extents.x + folga), caixa.min.y + 1.2f, z0 + dz);
                        var mundo = raiz.TransformPoint(local);
                        if (!AcharChao(mundo + Vector3.up * 1.5f, 6f, out var chao)) continue;
                        // Um pouco acima do chão: nascer encostado numa malha fina deixa a cápsula atravessá-la.
                        chao += Vector3.up * 0.1f;
                        if (!Cabe(chao, AlturaEmPe)) continue;
                        pe = chao;
                        olhar = raiz.TransformDirection(new Vector3(lado, 0f, 0f));
                        return true;
                    }
                }
            pe = olhar = default;
            return false;
        }

        internal static Bounds Carroceria() => Interior.Caixa;

        internal static bool DentroDaCarroceria(Vector3 mundo) => Interior.Dentro(mundo);

        static bool AcharChao(Vector3 de, float dist, out Vector3 ponto)
        {
            if (Physics.Raycast(de, Vector3.down, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                ponto = hit.point;
                return true;
            }
            ponto = default;
            return false;
        }

        static bool Cabe(Vector3 pe, float altura)
        {
            var a = pe + Vector3.up * (Raio + 0.06f);
            var b = pe + Vector3.up * (altura - Raio);
            // O próprio CharacterController é um colisor: ligado, a cápsula de teste esbarra nele e nunca "cabe"
            // (agachado, o jogador não levantava mais).
            bool cc = _cc != null && _cc.enabled;
            if (cc) _cc.enabled = false;
            bool livre = !Physics.CheckCapsule(a, b, Raio, ~0, QueryTriggerInteraction.Ignore);
            if (cc) _cc.enabled = true;
            return livre;
        }

        static void CriarCorpo(Vector3 pe)
        {
            _corpo = new GameObject("AndarAPe_Jogador");
            _corpo.hideFlags = HideFlags.DontSave;
            _corpo.transform.position = pe;
            _cc = _corpo.AddComponent<CharacterController>();
            _cc.radius = Raio;
            _cc.height = AlturaEmPe;
            _cc.center = new Vector3(0f, AlturaEmPe / 2f, 0f);
            _cc.stepOffset = 0.4f;
            _cc.slopeLimit = 50f;
            _cc.skinWidth = 0.03f;
            _cc.minMoveDistance = 0f;
        }

        // ---------- subir ----------

        internal static bool PertoDoOnibus(out float distancia)
        {
            distancia = float.MaxValue;
            if (_cc == null || Veiculo.Raiz == null) return false;
            if (_dentro) { distancia = 0f; return true; }
            var pe = _corpo.transform.position;
            if (DentroDaCarroceria(pe + Vector3.up * 1f)) { distancia = 0f; return true; }
            foreach (var p in Veiculo.Portas.Todas)
            {
                if (p.Jogo == null) continue;
                var d = p.Jogo.transform.position - pe;
                if (Mathf.Abs(d.y) > 3f) continue;
                d.y = 0f;
                distancia = Mathf.Min(distancia, d.magnitude);
            }
            // Ônibus sem portas conhecidas: a lateral da caixa.
            if (Veiculo.Portas.Todas.Count == 0)
            {
                var l = Veiculo.Raiz.InverseTransformPoint(pe);
                var c = Carroceria();
                float dx = Mathf.Max(0f, Mathf.Abs(l.x - c.center.x) - c.extents.x);
                float dz = Mathf.Max(0f, Mathf.Abs(l.z - c.center.z) - c.extents.z);
                distancia = Mathf.Sqrt(dx * dx + dz * dz);
            }
            return distancia <= Cfg.DistanciaParaEntrar.Value;
        }

        internal static bool PertoDoBanco()
        {
            if (!_dentro) return false;
            var d = _local - _localMotorista;
            return new Vector2(d.x, d.z).magnitude <= 1.6f;
        }

        internal static bool Subir(bool forcar)
        {
            if (!Andando) return true;
            if (!forcar && _dentro && !PertoDoBanco())
                return Recusar("Vá até o banco do motorista para dirigir");
            // De fora só dá para voltar a dirigir entrando pela porta e indo até o posto do motorista. Ônibus sem
            // caminhos de passageiros (não dá para andar dentro): G perto de uma porta leva direto ao banco.
            if (!forcar && !_dentro && Interior.TemCaminhos)
                return Recusar("Entre no ônibus e vá até o posto do motorista");
            if (!forcar && !_dentro && !PertoDoOnibus(out _))
                return Recusar("Chegue perto de uma porta do ônibus para subir");
            Terminar(true);
            Log.Msg($"subiu (câmera {MouseOrbitCSHARP.MEP_SelCamera})");
            return true;
        }

        static void Terminar(bool devolverCamera)
        {
            Andando = false;
            _dentro = false;
            Interacao.Limpar();
            Avisos.Definir(Dono, "entrar", false, Gravidade.Info, "");
            if (_corpo != null) Object.Destroy(_corpo);
            _corpo = null; _cc = null;
            if (devolverCamera && _cam != null && !_cam.WasCollected)
            {
                if (_paiCamera != null && !_paiCamera.WasCollected) _cam.SetParent(_paiCamera, true);
                if (_camera != null && !_camera.WasCollected)
                {
                    _camera.fieldOfView = _fovOriginal;
                    _camera.nearClipPlane = _nearOriginal;
                }
                var mmc = MepMobileControls.instance;
                if (mmc != null) mmc.SetCameraMotorista();
            }
            _cam = null; _camera = null; _paiCamera = null;
        }

        internal static void CenaMudou()
        {
            if (Andando) Terminar(false);
        }

        // ---------- a pé ----------

        internal static void Quadro()
        {
            if (!Andando) return;
            if (_cc == null || _cc.WasCollected || _cam == null || _cam.WasCollected || !Veiculo.Existe || Veiculo.Geracao != _geracao)
            {
                Log.Aviso("câmera ou ônibus trocados enquanto andava a pé: voltando");
                Terminar(true);
                return;
            }

            // Quem mantém estes sinalizadores é o Update do MouseOrbitCSHARP, desligado a pé. O som e outros
            // scripts do jogo os leem para saber se a câmera está dentro do ônibus.
            MouseOrbitCSHARP.tahNaCameraInterna = _dentro;
            MouseOrbitCSHARP.tahNaCameraLivre = true;


            bool pausado = MepMobileControls.taPausado;
            if (!pausado)
            {
                // A pé o teclado é do mod. O Esc ainda pausa: como o Rewired está suspenso, a pausa vem daqui.
                Teclado.SuspenderJogo(Dono);
                Mouse.Prender(Dono);
                if (!_pausadoAntes && Input.GetKeyDown(KeyCode.Escape))
                {
                    var mmc = MepMobileControls.instance;
                    if (mmc != null) mmc.ClicouPausaDespausa(false);
                }
            }
            _pausadoAntes = pausado;
            if (pausado) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (!Mouse.Solto)
            {
                float s = Cfg.Sensibilidade.Value;
                float dy = Input.GetAxisRaw("Mouse Y") * s;
                float dx = Input.GetAxisRaw("Mouse X") * s;
                if (_dentro) _yawLocal = Mathf.Repeat(_yawLocal + dx, 360f);
                else _yaw = Mathf.Repeat(_yaw + dx, 360f);
                _pitch = Mathf.Clamp(_pitch + (Cfg.InverterY.Value ? dy : -dy), -85f, 85f);
            }

            if (_dentro) MoverDentro(dt);
            else Mover(dt);
            Interacao.Quadro(_cam, !Mouse.Solto);
            Dica();
        }

        static void Mover(float dt)
        {
            var t = _corpo.transform;
            bool painel = Mouse.Solto;   // painel de algum mod aberto: as teclas são dele
            Vector2 ent = EntradaTeste;
            if (!painel)
            {
                if (Cfg.Frente.Segurando) ent.y += 1f;
                if (Cfg.Tras.Segurando) ent.y -= 1f;
                if (Cfg.Direita.Segurando) ent.x += 1f;
                if (Cfg.Esquerda.Segurando) ent.x -= 1f;
            }
            ent = Vector2.ClampMagnitude(ent, 1f);

            // Agachar: a cápsula encolhe; só levanta se houver espaço acima.
            bool querAgachar = AgacharTeste || (!painel && Cfg.Agachar.Segurando);
            if (querAgachar != _agachado && (querAgachar || Cabe(t.position, AlturaEmPe)))
            {
                _agachado = querAgachar;
                float h = _agachado ? AlturaAgachado : AlturaEmPe;
                _cc.height = h;
                _cc.center = new Vector3(0f, h / 2f, 0f);
            }

            bool correr = !_agachado && (CorrerTeste || (!painel && Cfg.Correr.Segurando));
            float vMax = _agachado ? Cfg.VelocidadeAgachado.Value : correr ? Cfg.VelocidadeCorrer.Value : Cfg.VelocidadeAndar.Value;
            var giro = Quaternion.Euler(0f, _yaw, 0f);
            var alvo = giro * new Vector3(ent.x, 0f, ent.y) * vMax;

            bool chao = _cc.isGrounded;
            var horiz = new Vector3(_vel.x, 0f, _vel.z);
            horiz = Vector3.MoveTowards(horiz, alvo, (chao ? 12f : 2f) * vMax * dt);
            _vel.x = horiz.x; _vel.z = horiz.z;

            bool pular = PularTeste || (!painel && Cfg.Pular.Segurando);
            if (chao)
            {
                _ultimoChao = t.position;
                if (_vel.y < 0f) _vel.y = -2f;   // gruda no chão em descidas
                if (pular && !_pularAntes && !_agachado) _vel.y = Mathf.Sqrt(2f * G * Mathf.Max(0.05f, Cfg.ForcaPulo.Value));
            }
            _pularAntes = pular;
            _vel.y -= G * dt;

            var flags = _cc.Move(_vel * dt);
            if ((flags & CollisionFlags.Above) != 0 && _vel.y > 0f) _vel.y = 0f;

            if (t.position.y < _ultimoChao.y - QuedaMaxima)
            {
                Log.Aviso($"caiu por um buraco sem colisor em {Fmt(t.position)}: voltando para {Fmt(_ultimoChao)}");
                Teleportar(_ultimoChao + Vector3.up * 0.3f);
                _vel = Vector3.zero;
            }

            if (ent.sqrMagnitude > 0.01f && TentarEntrar(alvo)) return;

            // Balanço da cabeça: fase pela distância andada no chão.
            float v = horiz.magnitude;
            if (chao && v > 0.2f) _passo += v * dt / (correr ? 0.85f : 0.65f) * Mathf.PI;
            float olhosAlvo = _agachado ? Cfg.AlturaOlhos.Value - (AlturaEmPe - AlturaAgachado) : Cfg.AlturaOlhos.Value;
            _olhos = Mathf.MoveTowards(_olhos, olhosAlvo, 3f * dt);
        }

        // Fora, encostado no vão de uma porta aberta e andando para dentro: passa a andar no ônibus.
        static bool TentarEntrar(Vector3 alvoMundo)
        {
            var raiz = Veiculo.Raiz;
            var c = Interior.Caixa;
            var l = raiz.InverseTransformPoint(_corpo.transform.position);
            var dir = raiz.InverseTransformDirection(alvoMundo);
            foreach (var d in Interior.Portas)
            {
                if (!d.Porta.Aberta) continue;
                float fora = d.Lado * (l.x - c.center.x) - c.extents.x;
                if (fora > 0.8f || fora < -0.4f || l.z < d.Z0 + 0.1f || l.z > d.Z1 - 0.1f) continue;
                if (l.y < c.min.y - 1.5f || l.y > c.min.y + 1.2f) continue;
                if (-d.Lado * dir.x < 0.3f * dir.magnitude) continue;   // não está indo para dentro
                _dentro = true;
                _cc.enabled = false;
                _local = new Vector3(c.center.x + d.Lado * (c.extents.x - 0.2f), l.y, Mathf.Clamp(l.z, d.Z0 + 0.15f, d.Z1 - 0.15f));
                _yawLocal = Mathf.Repeat(_yaw - YawOnibus(), 360f);
                _velLocal = raiz.InverseTransformDirection(new Vector3(_vel.x, 0f, _vel.z)); _velLocal.y = 0f;
                _altura = 0f; _vy = 0f;
                Log.Msg($"entrou pela {d.Nome} em {Fmt(_local)}");
                return true;
            }
            return false;
        }

        static void MoverDentro(float dt)
        {
            var raiz = Veiculo.Raiz;
            var c = Interior.Caixa;
            bool painel = Mouse.Solto;
            Vector2 ent = EntradaTeste;
            if (!painel)
            {
                if (Cfg.Frente.Segurando) ent.y += 1f;
                if (Cfg.Tras.Segurando) ent.y -= 1f;
                if (Cfg.Direita.Segurando) ent.x += 1f;
                if (Cfg.Esquerda.Segurando) ent.x -= 1f;
            }
            ent = Vector2.ClampMagnitude(ent, 1f);
            _agachado = !painel && Cfg.Agachar.Segurando;
            bool correr = !_agachado && (CorrerTeste || (!painel && Cfg.Correr.Segurando));
            float vMax = _agachado ? Cfg.VelocidadeAgachado.Value : correr ? Cfg.VelocidadeCorrer.Value : Cfg.VelocidadeAndar.Value;
            var alvo = Quaternion.Euler(0f, _yawLocal, 0f) * new Vector3(ent.x, 0f, ent.y) * vMax;
            _velLocal = Vector3.MoveTowards(_velLocal, alvo, 12f * vMax * dt);

            var n = _local + _velLocal * dt;
            const float Folga = 0.42f;   // dos olhos à parede: mais perto, a câmera entra no vidro da porta
            float xMin = c.min.x + Folga, xMax = c.max.x - Folga, zMin = c.min.z + Folga, zMax = c.max.z - Folga;
            var porta = Interior.PortaAbertaEm(n.z);
            if (porta != null)
            {
                // No vão da porta aberta a parede some; passando dela, desce do ônibus.
                if (porta.Lado > 0f) xMax = c.max.x + 0.2f; else xMin = c.min.x - 0.2f;
                if (porta.Lado * (n.x - c.center.x) > c.extents.x - 0.05f && TentarSair(porta, n)) return;
            }
            n = Interior.NoCorredor(n, LarguraCorredor);
            float x = Mathf.Clamp(n.x, xMin, xMax), z = Mathf.Clamp(n.z, zMin, zMax);
            n.x = x; n.z = z;
            // Velocidade do que realmente andou: encostado, escorrega rente à borda em vez de acumular.
            _velLocal = new Vector3(n.x - _local.x, 0f, n.z - _local.z) / dt;

            // Degraus: o pé acompanha o piso dos caminhos, sem pular de uma vez.
            float piso = Interior.Piso(n);
            n.y = Mathf.MoveTowards(_local.y, piso, 2.2f * dt);
            bool pular = PularTeste || (!painel && Cfg.Pular.Segurando);
            if (_altura <= 0f && pular && !_pularAntes && !_agachado)
                _vy = Mathf.Sqrt(2f * G * Mathf.Clamp(Cfg.ForcaPulo.Value, 0.05f, 0.35f));   // teto baixo
            _pularAntes = pular;
            _altura += _vy * dt;
            _vy -= G * dt;
            if (_altura <= 0f) { _altura = 0f; _vy = 0f; }
            _local = n;
            _corpo.transform.position = raiz.TransformPoint(_local);

            float v = new Vector2(_velLocal.x, _velLocal.z).magnitude;
            if (_altura <= 0f && v > 0.2f) _passo += v * dt / (correr ? 0.85f : 0.65f) * Mathf.PI;
            float olhosAlvo = _agachado ? Cfg.AlturaOlhos.Value - (AlturaEmPe - AlturaAgachado) : Cfg.AlturaOlhos.Value;
            _olhos = Mathf.MoveTowards(_olhos, olhosAlvo, 3f * dt);
        }

        // Do vão da porta para a calçada: o chão de fora por raio, e a cápsula precisa caber.
        static bool TentarSair(Interior.PortaDentro porta, Vector3 n)
        {
            var raiz = Veiculo.Raiz;
            var fora = raiz.TransformPoint(new Vector3(Interior.Parede(porta.Lado) + porta.Lado * 0.45f, n.y, n.z));
            if (!AcharChao(fora + Vector3.up * 1.2f, 3.5f, out var chao)) return false;
            chao += Vector3.up * 0.05f;
            if (!Cabe(chao, AlturaEmPe)) return false;
            _dentro = false;
            _cc.enabled = true;
            Teleportar(chao);
            _yaw = Mathf.Repeat(_yawLocal + YawOnibus(), 360f);
            _vel = raiz.TransformDirection(_velLocal); _vel.y = 0f;
            _ultimoChao = chao;
            Log.Msg($"desceu pela {porta.Nome} em {Fmt(chao)}");
            return true;
        }

        static void Teleportar(Vector3 p)
        {
            _cc.enabled = false;
            _corpo.transform.position = p;
            _cc.enabled = true;
        }

        // A faixa verde da câmera livre é a notificação do jogo (TelaDeNotificacao/Canvas/Panel2/Text2), não o
        // fundoCanvasStatus do MepMobileControls. Acha o texto uma vez ao descer e esconde o painel enquanto ele
        // mostrar a mensagem da câmera livre; outra notificação do jogo volta a aparecer normalmente.
        static UnityEngine.UI.Text _faixa;
        static float _procurarFaixaAte;

        static void EsconderFaixa()
        {
            if ((_faixa == null || _faixa.WasCollected) && Time.unscaledTime < _procurarFaixaAte)
                foreach (var t in Object.FindObjectsOfType<UnityEngine.UI.Text>())
                    if (t.text != null && t.text.Contains("WASD") && t.transform.parent != null
                        && !t.transform.root.name.StartsWith("PBSLoaderCommonInterface")) { _faixa = t; _procurarFaixaAte = 0f; break; }
            if (_faixa == null || _faixa.WasCollected) return;
            var painel = _faixa.transform.parent.gameObject;
            if (painel.activeSelf && _faixa.text != null && _faixa.text.Contains("WASD")) painel.SetActive(false);
        }

        static void Dica()
        {
            if (Time.unscaledTime < _proximaDica) return;
            _proximaDica = Time.unscaledTime + 0.25f;
            var tecla = Cfg.TeclaDescer.Combinacao.ToString();
            bool mostrar = _dentro ? PertoDoBanco() : !Interior.TemCaminhos && PertoDoOnibus(out _);
            Avisos.Definir(Dono, "entrar", mostrar && tecla.Length > 0, Gravidade.Info, _dentro ? $"{tecla}: dirigir" : $"{tecla}: subir no ônibus");
        }

        internal static void PosicionarCamera()
        {
            if (!Andando || _cc == null || _cam == null || _cam.WasCollected) return;
            // O Update do jogo reativa a faixa da câmera livre por alguns segundos; aqui (LateUpdate) vem depois.
            EsconderFaixa();
            float bal = 0f, lado = 0f;
            float k = Cfg.Balanco.Value;
            if (k > 0f)
            {
                float v = _dentro ? new Vector2(_velLocal.x, _velLocal.z).magnitude : new Vector2(_vel.x, _vel.z).magnitude;
                float amp = Mathf.Clamp01(v / Mathf.Max(0.1f, Cfg.VelocidadeAndar.Value)) * k;
                bal = Mathf.Abs(Mathf.Sin(_passo)) * 0.035f * amp;
                lado = Mathf.Cos(_passo) * 0.02f * amp;
            }
            var raiz = Veiculo.Raiz;
            if (_dentro && raiz != null)
            {
                // Dentro, a cabeça vai com o ônibus (inclinação e curvas incluídas).
                var g = raiz.rotation * Quaternion.Euler(_pitch, _yawLocal, 0f);
                _cam.position = raiz.TransformPoint(_local) + raiz.up * (_olhos + _altura + bal - 0.02f * k) + g * Vector3.right * lado;
                _cam.rotation = g;
                return;
            }
            var giro = Quaternion.Euler(_pitch, _yaw, 0f);
            _cam.position = _corpo.transform.position + Vector3.up * (_olhos + bal - 0.02f * k) + giro * Vector3.right * lado;
            _cam.rotation = giro;
        }

        // ---------- diagnóstico ----------

        internal static string Fmt(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "({0:0.00}, {1:0.00}, {2:0.00})", v.x, v.y, v.z);

        internal static string Estado()
        {
            var sb = new StringBuilder();
            sb.Append($"andando {Andando}, câmera do jogo {MouseOrbitCSHARP.MEP_SelCamera}, livre {MouseOrbitCSHARP.tahNaCameraLivre}, interna {MouseOrbitCSHARP.tahNaCameraInterna}");
            sb.Append($", teclado suspenso {Teclado.JogoSuspenso}, freio de mão {Veiculo.Pedais.FreioDeMaoPuxado}");
            if (_cc != null && _dentro)
                sb.Append($", DENTRO local {Fmt(_local)} piso {Interior.Piso(_local):0.00}, yaw local {_yawLocal:0}, porta alvo {(Interacao.Alvo == null ? "-" : Interacao.Texto)}");
            else if (_cc != null)
                sb.Append($", FORA pé {Fmt(_corpo.transform.position)}, no chão {_cc.isGrounded}, vel {Fmt(_vel)}, yaw {_yaw:0}, porta alvo {(Interacao.Alvo == null ? "-" : Interacao.Texto)}");
            var orbit = MouseOrbitCSHARP.instance;
            if (orbit != null)
            {
                var ct = orbit.transform;
                sb.Append($", câmera em {Fmt(ct.position)} pai {(ct.parent != null ? ct.parent.name : "nenhum")}");
                var cm = Camera.main;
                sb.Append($", Camera.main {(cm == null ? "nula" : cm.transform == ct ? "= orbit" : cm.name)}");
            }
            return sb.ToString();
        }
    }
}
