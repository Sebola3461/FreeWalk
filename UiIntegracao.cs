using System;
using MelonLoader;

[assembly: MelonAdditionalDependencies("PBSLoaderCommonUtils", "PBSLoaderCommonInterface")]

namespace AndarAPe
{
    // Coloca o mod no menu da PBSLoaderCommonInterface (ações sem atalho de teclado e sliders com faixa). Ela é base do PBSLoader,
    // sempre instalada junto com a PBSLoaderCommonUtils.
    internal static class UiIntegracao
    {
        internal static void Registrar()
        {
            try { Fazer(); }
            catch (Exception e) { MelonLogger.Warning("Integração com a PBSLoaderCommonInterface falhou: " + e.Message); }
        }

        static void Fazer()
        {
            const string C = Cfg.M;
            PBSLoaderCommonInterface.PainelMods.Nome(C, "Andar a pé");
            PBSLoaderCommonInterface.PainelMods.Acao(C, "Descer ou subir no ônibus", Pedestre.Alternar, "Desce com o ônibus parado; sobe perto de uma porta");
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "VelocidadeAndar", 0.5f, 3f, 0.1f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "VelocidadeCorrer", 2f, 10f, 0.1f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "AlturaOlhos", 1.2f, 2f, 0.01f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Sensibilidade", 0.5f, 10f, 0.1f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "CampoDeVisao", 0f, 110f, 1f);
            PBSLoaderCommonInterface.PainelMods.Faixa(C, "Balanco", 0f, 2f, 0.1f);
        }
    }
}
