using System.Globalization;
using System.Text;

namespace Eleu.Documento;

/// <summary>
/// Converte un <see cref="ContenutoDocumento"/> in RTF da caricare nel campo di testo.
/// I titoli usano gli stili predefiniti "heading N" di RichEdit, così restano riconoscibili.
/// </summary>
public static class RtfScrittore
{
    private const int IndiceSymbol = 1;

    public static string Scrivi(ContenutoDocumento contenuto, string carattere = "Segoe UI", float dimensioneBase = ContenutoDocumento.DimensioneBase)
    {
        var segmenti = contenuto.Paragrafi.SelectMany(p => p.Segmenti).ToList();

        // Tabella dei caratteri: 0 = quello predefinito, 1 = Symbol (per i pallini degli elenchi), poi gli altri usati.
        var caratteri = new List<string> { carattere, "Symbol" };
        foreach (var nome in segmenti.Select(s => s.Carattere).OfType<string>())
            if (!caratteri.Contains(nome, StringComparer.OrdinalIgnoreCase))
                caratteri.Add(nome);

        // Tabella dei colori: l'indice 0 è "automatico".
        var colori = segmenti.SelectMany(s => new[] { s.Colore, s.Sfondo }).OfType<int>().Distinct().ToList();

        var rtf = new StringBuilder();
        rtf.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1");
        rtf.Append(@"{\fonttbl");
        for (int i = 0; i < caratteri.Count; i++)
        {
            rtf.Append(@"{\f").Append(i).Append(i == IndiceSymbol ? @"\fnil\fcharset2 " : @"\fnil\fcharset0 ");
            Escapa(caratteri[i], rtf);
            rtf.Append(";}");
        }
        rtf.Append('}');
        rtf.Append(@"{\colortbl ;");
        foreach (var colore in colori)
            rtf.Append(@"\red").Append((colore >> 16) & 0xFF).Append(@"\green").Append((colore >> 8) & 0xFF).Append(@"\blue").Append(colore & 0xFF).Append(';');
        rtf.Append('}');
        rtf.Append(@"{\stylesheet{ Normal;}");
        for (int livello = 1; livello <= 9; livello++)
            rtf.Append(@"{\s").Append(livello).Append(@" heading ").Append(livello).Append(";}");
        rtf.Append("}\r\n");

        int numero = 0;
        var paragrafi = contenuto.Paragrafi;
        for (int indice = 0; indice < paragrafi.Count; indice++)
        {
            var paragrafo = paragrafi[indice];
            var cella = paragrafo.Cella;
            var precedente = indice > 0 ? paragrafi[indice - 1].Cella : null;
            var seguente = indice + 1 < paragrafi.Count ? paragrafi[indice + 1].Cella : null;
            if (cella is not null && !cella.StessaRiga(precedente))
                ScriviInizioRiga(paragrafi, indice, rtf);

            numero = paragrafo.Numerato ? numero + 1 : 0;
            ScriviParagrafo(paragrafo, numero, rtf);

            float dimensioneParagrafo = paragrafo.LivelloTitolo > 0
                ? ContenutoDocumento.DimensioneTitolo(paragrafo.LivelloTitolo)
                : dimensioneBase;

            // L'interruzione di pagina è il carattere di salto pagina all'inizio del paragrafo, come in Word.
            if (paragrafo.SaltoPagina)
                rtf.Append(@"\page ");

            if (paragrafo.Segmenti.Count == 0)
                rtf.Append(@"\f0\fs").Append(MezziPunti(dimensioneParagrafo)).Append(' ');

            foreach (var segmento in paragrafo.Segmenti)
            {
                int indiceCarattere = segmento.Carattere is null
                    ? 0
                    : caratteri.FindIndex(c => c.Equals(segmento.Carattere, StringComparison.OrdinalIgnoreCase));
                rtf.Append(@"{\f").Append(indiceCarattere).Append(@"\fs").Append(MezziPunti(segmento.Dimensione ?? dimensioneParagrafo));
                if (segmento.Grassetto || paragrafo.LivelloTitolo > 0)
                    rtf.Append(@"\b");
                if (segmento.Corsivo)
                    rtf.Append(@"\i");
                if (segmento.Sottolineato)
                    rtf.Append(@"\ul");
                if (segmento.Barrato)
                    rtf.Append(@"\strike");
                if (segmento.Posizione == PosizioneTesto.Apice)
                    rtf.Append(@"\super");
                else if (segmento.Posizione == PosizioneTesto.Pedice)
                    rtf.Append(@"\sub");
                if (segmento.Colore is int colore)
                    rtf.Append(@"\cf").Append(colori.IndexOf(colore) + 1);
                if (segmento.Sfondo is int sfondo)
                    rtf.Append(@"\highlight").Append(colori.IndexOf(sfondo) + 1);
                if (segmento.Spaziatura != 0)
                    rtf.Append(@"\expndtw").Append(Twip(segmento.Spaziatura));
                rtf.Append(' ');
                Escapa(segmento.Testo, rtf);
                rtf.Append('}');
            }
            // In tabella l'ultimo paragrafo della cella finisce con \cell, l'ultima cella della riga anche con \row.
            if (cella is null || cella.StessaCella(seguente))
                rtf.Append("\\par\r\n");
            else
                rtf.Append(cella.StessaRiga(seguente) ? "\\cell " : "\\cell\\row\r\n");
        }

        rtf.Append('}');
        return rtf.ToString();
    }

    /// <summary>Larghezza di una colonna senza larghezza scelta: 4 cm, in twip.</summary>
    private const int LarghezzaColonnaPredefinita = 2268;

    /// <summary>
    /// L'inizio di una riga di tabella: \trowd, l'eventuale \trhdr (riga di intestazione) e per ogni cella
    /// i bordi sottili e il suo bordo destro (\cellx), sommando le larghezze delle colonne.
    /// </summary>
    private static void ScriviInizioRiga(IReadOnlyList<Paragrafo> paragrafi, int indice, StringBuilder rtf)
    {
        var prima = paragrafi[indice].Cella!;
        var celle = paragrafi.Skip(indice).Select(p => p.Cella).TakeWhile(c => prima.StessaRiga(c))
            .DistinctBy(c => c!.Colonna).ToList();
        rtf.Append(@"\trowd\trgaph108\trleft0");
        if (prima.Intestazione)
            rtf.Append(@"\trhdr");
        int destra = 0;
        foreach (var c in celle)
        {
            destra += c!.Larghezza > 0 ? Twip(c.Larghezza) : LarghezzaColonnaPredefinita;
            rtf.Append(@"\clbrdrl\brdrw10\brdrs\clbrdrt\brdrw10\brdrs\clbrdrr\brdrw10\brdrs\clbrdrb\brdrw10\brdrs");
            rtf.Append(@"\cellx").Append(destra);
        }
        rtf.Append("\r\n");
    }

    private static void ScriviParagrafo(Paragrafo paragrafo, int numero, StringBuilder rtf)
    {
        rtf.Append(@"\pard");
        if (paragrafo.Cella is not null)
            rtf.Append(@"\intbl");
        if (paragrafo.LivelloTitolo > 0)
            rtf.Append(@"\s").Append(paragrafo.LivelloTitolo);
        rtf.Append(paragrafo.Allineamento switch
        {
            Allineamento.Centro => @"\qc",
            Allineamento.Destra => @"\qr",
            Allineamento.Giustificato => @"\qj",
            _ => @"\ql",
        });

        float sinistro = paragrafo.RientroSinistro;
        float primaRiga = paragrafo.RientroPrimaRiga;
        if (paragrafo.Numerato)
        {
            rtf.Append(@"{\pntext\f0 ").Append(numero).Append(@".\tab}{\*\pn\pnlvlbody\pnf0\pnindent0\pnstart1\pndec{\pntxta.}}");
            sinistro += 18;
            primaRiga = -18;
        }
        else if (paragrafo.Elenco)
        {
            rtf.Append(@"{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'B7}}");
            sinistro += 18;
            primaRiga = -18;
        }

        if (sinistro != 0)
            rtf.Append(@"\li").Append(Twip(sinistro));
        if (paragrafo.RientroDestro != 0)
            rtf.Append(@"\ri").Append(Twip(paragrafo.RientroDestro));
        if (primaRiga != 0)
            rtf.Append(@"\fi").Append(Twip(primaRiga));
        if (paragrafo.SpazioPrima != 0)
            rtf.Append(@"\sb").Append(Twip(paragrafo.SpazioPrima));
        if (paragrafo.SpazioDopo != 0)
            rtf.Append(@"\sa").Append(Twip(paragrafo.SpazioDopo));
        if (paragrafo.Interlinea != 1f)
            rtf.Append(@"\sl").Append((int)Math.Round(paragrafo.Interlinea * 240)).Append(@"\slmult1");
    }

    /// <summary>
    /// Aggiunge a un RTF (es. quello del campo di testo) le impostazioni di pagina, l'intestazione e il piè di pagina,
    /// subito prima del primo paragrafo. Il campo di testo non le conserva: si aggiungono solo nei file salvati.
    /// </summary>
    public static string ConPagina(string rtf, ImpostazioniPagina pagina)
    {
        var (larghezza, altezza) = pagina.Effettive;
        var parti = new StringBuilder();
        parti.Append(@"\paperw").Append(Twip(larghezza)).Append(@"\paperh").Append(Twip(altezza))
            .Append(@"\margl").Append(Twip(pagina.MargineSinistro)).Append(@"\margr").Append(Twip(pagina.MargineDestro))
            .Append(@"\margt").Append(Twip(pagina.MargineSopra)).Append(@"\margb").Append(Twip(pagina.MargineSotto));
        if (pagina.Orizzontale)
            parti.Append(@"\landscape");
        parti.Append(@"\headery").Append(Twip(pagina.DistanzaIntestazione)).Append(@"\footery").Append(Twip(pagina.DistanzaPiede));
        if (pagina.EscludiPrimaPagina)
            parti.Append(@"\titlepg{\headerf\pard\par}{\footerf\pard\par}");
        ScriviBordo("header", pagina.Intestazione, parti);
        ScriviBordo("footer", pagina.PiePagina, parti);
        parti.Append("\r\n");

        int primoParagrafo = rtf.IndexOf(@"\pard", StringComparison.Ordinal);
        return primoParagrafo < 0 ? rtf : rtf.Insert(primoParagrafo, parti.ToString());
    }

    /// <summary>Intestazione o piè di pagina: le variabili diventano campi (PAGE, NUMPAGES, ...) che ogni programma aggiorna.</summary>
    private static void ScriviBordo(string destinazione, string modello, StringBuilder rtf)
    {
        if (modello.Length == 0)
            return;
        rtf.Append('{').Append('\\').Append(destinazione).Append(@"\pard\qc ");
        foreach (var (testo, campo) in ImpostazioniPagina.Pezzi(modello))
        {
            if (campo is null)
                Escapa(testo, rtf);
            else
                rtf.Append(@"{\field{\*\fldinst ").Append(campo).Append(@"}{\fldrslt 1}}");
        }
        rtf.Append(@"\par}");
    }

    /// <summary>Punti in twip (ventesimi di punto), l'unità di RTF e DOCX.</summary>
    public static int Twip(float punti) => (int)Math.Round(punti * 20);

    private static string MezziPunti(float punti) =>
        ((int)Math.Round(punti * 2)).ToString(CultureInfo.InvariantCulture);

    private static void Escapa(string testo, StringBuilder rtf)
    {
        foreach (char c in testo)
        {
            switch (c)
            {
                case '\\' or '{' or '}':
                    rtf.Append('\\').Append(c);
                    break;
                case '\t':
                    rtf.Append(@"\tab ");
                    break;
                case '\n' or '\r':
                    break;
                case < (char)0x80:
                    rtf.Append(c);
                    break;
                default:
                    rtf.Append(@"\u").Append((short)c).Append('?');
                    break;
            }
        }
    }
}
