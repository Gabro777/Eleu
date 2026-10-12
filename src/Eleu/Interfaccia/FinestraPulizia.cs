using Eleu.Correzione;

namespace Eleu.Interfaccia;

/// <summary>Strumenti > Pulizia del testo: cosa ripulire, e se solo nella selezione.</summary>
public sealed class FinestraPulizia : Modulo
{
    private readonly CheckBox _spaziDoppi, _spaziAiBordi, _righeVuote, _sillabazione, _paragrafiSpezzati, _soloSelezione;

    public FinestraPulizia(OpzioniPulizia opzioni, bool selezione) : base("Pulizia del testo", 380)
    {
        CheckBox Casella(string testo, bool spuntata) => RigaIntera(new CheckBox { Text = testo, Checked = spuntata, AutoSize = true });
        _spaziDoppi = Casella("&Spazi doppi tra le parole", opzioni.SpaziDoppi);
        _spaziAiBordi = Casella("Spazi a &inizio e fine paragrafo", opzioni.SpaziAiBordi);
        _righeVuote = Casella("&Righe vuote ripetute (ne resta una)", opzioni.RigheVuote);
        _sillabazione = Casella("Parole &divise dal trattino a fine riga", opzioni.Sillabazione);
        _paragrafiSpezzati = Casella("&Paragrafi spezzati a metà frase (testo copiato da PDF)", opzioni.ParagrafiSpezzati);
        _soloSelezione = Casella("Solo nella s&elezione", selezione);
        _soloSelezione.Enabled = selezione;
        AggiungiPulsanti();
        ActiveControl = _spaziDoppi;
    }

    public OpzioniPulizia Opzioni => new(_spaziDoppi.Checked, _spaziAiBordi.Checked, _righeVuote.Checked, _sillabazione.Checked, _paragrafiSpezzati.Checked);

    public bool SoloSelezione => _soloSelezione.Enabled && _soloSelezione.Checked;
}
