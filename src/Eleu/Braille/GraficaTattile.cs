using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Eleu.Braille;

/// <summary>Come diventa in rilievo un'immagine: solo i contorni (di solito più leggibili) o le zone scure piene.</summary>
public enum TrattoTattile
{
    Contorni,
    Riempito,
}

/// <param name="Soglia">Quanto un punto deve staccarsi dallo sfondo per andare in rilievo: da 1 (molto) a 254 (pochissimo); 0 = la sceglie Eleu.</param>
/// <param name="Inverti">In rilievo lo sfondo invece della figura.</param>
public sealed record OpzioniGrafica(TrattoTattile Tratto = TrattoTattile.Contorni, int Soglia = 0, bool Inverti = false);

/// <summary>
/// Immagini in rilievo. Due strade:
/// <list type="bullet">
/// <item>con la stampante Braille: l'immagine diventa una griglia di celle, ogni punto della cella è un punto
/// in rilievo (grafica a punti, nel file BRF come il testo);</item>
/// <item>con la carta a microcapsule (o "a rilievo"): si stampa un disegno nero su bianco, semplice e con tratti
/// spessi, che il fornetto fa gonfiare; la didascalia è in punti Braille disegnati, che si gonfiano anch'essi.</item>
/// </list>
/// Le misure sono quelle reali del Braille: punti a 2,5 mm, celle ogni 6,2 mm, righe ogni 10 mm.
/// </summary>
public static class GraficaTattile
{
    public const double PassoPunto = 2.5, PassoCella = 6.2, PassoRiga = 10.0;
    private const int PixelPerMm = 4;

    /// <summary>Quante celle e righe Braille occupa l'immagine, senza superare i massimi e senza deformarla.</summary>
    public static (int Celle, int Righe) Misure(int larghezza, int altezza, int celleMassime, int righeMassime)
    {
        double w = celleMassime * PassoCella;
        double h = w * altezza / Math.Max(1, larghezza);
        if (h > righeMassime * PassoRiga)
        {
            h = righeMassime * PassoRiga;
            w = h * larghezza / Math.Max(1, altezza);
        }
        return (Math.Clamp((int)Math.Ceiling(w / PassoCella), 1, celleMassime), Math.Clamp((int)Math.Ceiling(h / PassoRiga), 1, righeMassime));
    }

    /// <summary>
    /// L'immagine come righe di celle Braille (caratteri da U+2800): al massimo <paramref name="celleMassime"/>
    /// per riga e <paramref name="righeMassime"/> righe, al centro della riga.
    /// </summary>
    public static List<string> InCelle(Image immagine, int celleMassime, int righeMassime, OpzioniGrafica opzioni)
    {
        var (celle, righe) = Misure(immagine.Width, immagine.Height, celleMassime, righeMassime);
        // L'area di tutta la riga (per centrare), alta quanto le righe necessarie.
        double larghezzaMm = celleMassime * PassoCella, altezzaMm = righe * PassoRiga;
        double scala = Math.Min(celle * PassoCella / immagine.Width, altezzaMm / immagine.Height);
        double wMm = immagine.Width * scala, hMm = immagine.Height * scala;
        var area = new RectangleF((float)((larghezzaMm - wMm) / 2), 0, (float)wMm, (float)hMm);

        int lp = (int)Math.Ceiling(larghezzaMm * PixelPerMm), ap = (int)Math.Ceiling(altezzaMm * PixelPerMm);
        // I contorni spessi come la distanza tra due punti: così una linea non cade tra un punto e l'altro.
        var maschera = Maschera(immagine, lp, ap, Scala(area, PixelPerMm), opzioni, spessore: (int)(PassoPunto * PixelPerMm));
        double minimo = opzioni.Tratto == TrattoTattile.Contorni ? 0.25 : 0.5;

        var risultato = new List<string>(righe);
        for (int r = 0; r < righe; r++)
        {
            var riga = new char[celleMassime];
            for (int c = 0; c < celleMassime; c++)
            {
                int punti = 0;
                for (int dc = 0; dc < 2; dc++)
                    for (int dr = 0; dr < 3; dr++)
                    {
                        // Ogni punto guarda la sua parte di cella (metà in larghezza, un terzo in altezza): le
                        // zone tra un punto e l'altro e tra una riga e l'altra non restano fuori.
                        double x = c * PassoCella + dc * PassoCella / 2, y = r * PassoRiga + dr * PassoRiga / 3;
                        if (Copertura(maschera, x, y, PassoCella / 2, PassoRiga / 3) >= minimo)
                            punti |= 1 << (dc * 3 + dr); // punti 1-2-3 a sinistra, 4-5-6 a destra
                    }
                riga[c] = (char)(TraduttoreBraille.CellaVuota + punti);
            }
            risultato.Add(new string(riga));
        }
        return risultato;
    }

    /// <summary>La parte di "nero" nel rettangolo di un punto (angolo in alto a sinistra in x, y; misure in mm).</summary>
    private static double Copertura(bool[,] maschera, double xMm, double yMm, double larghezzaMm, double altezzaMm)
    {
        int x0 = (int)(xMm * PixelPerMm), y0 = (int)(yMm * PixelPerMm);
        int x1 = (int)((xMm + larghezzaMm) * PixelPerMm), y1 = (int)((yMm + altezzaMm) * PixelPerMm);
        int pieni = 0, tutti = 0;
        for (int y = y0; y < Math.Min(y1, maschera.GetLength(0)); y++)
            for (int x = x0; x < Math.Min(x1, maschera.GetLength(1)); x++)
            {
                tutti++;
                if (maschera[y, x])
                    pieni++;
            }
        return tutti == 0 ? 0 : (double)pieni / tutti;
    }

    /// <summary>
    /// Le pagine per il file BRF: in cima la didascalia (già in Braille, spezzata sulle parole) e una riga vuota,
    /// poi l'immagine; le righe in più vanno sulla pagina dopo.
    /// </summary>
    public static List<List<string>> Pagine(IReadOnlyList<string> immagine, int celle, int righePerPagina, string? didascalia)
    {
        var righe = new List<string>();
        if (!string.IsNullOrWhiteSpace(didascalia?.Replace(TraduttoreBraille.CellaVuota, ' ')))
        {
            righe.AddRange(AndaACapo(didascalia!, celle));
            righe.Add("");
        }
        righe.AddRange(immagine.Select(r => r.TrimEnd(TraduttoreBraille.CellaVuota)));
        return righe.Chunk(Math.Max(1, righePerPagina)).Select(p => p.ToList()).ToList();
    }

    /// <summary>Un testo Braille spezzato in righe di al massimo <paramref name="celle"/> celle, tra una parola e l'altra.</summary>
    public static List<string> AndaACapo(string braille, int celle)
    {
        var righe = new List<string>();
        var riga = "";
        foreach (var parola in braille.Split(TraduttoreBraille.CellaVuota, StringSplitOptions.RemoveEmptyEntries))
        {
            var pezzo = parola;
            while (pezzo.Length > celle)
            {
                if (riga.Length > 0)
                    righe.Add(riga);
                righe.Add(pezzo[..celle]);
                riga = "";
                pezzo = pezzo[celle..];
            }
            if (riga.Length == 0)
                riga = pezzo;
            else if (riga.Length + 1 + pezzo.Length <= celle)
                riga += TraduttoreBraille.CellaVuota + pezzo;
            else
            {
                righe.Add(riga);
                riga = pezzo;
            }
        }
        if (riga.Length > 0)
            righe.Add(riga);
        return righe;
    }

    /// <summary>
    /// Il disegno per la carta a microcapsule: nero puro su bianco, tratti spessi un millimetro e mezzo, alla
    /// risoluzione di stampa (10 punti per millimetro). La didascalia in Braille è disegnata in punti veri,
    /// in cima, così si legge con le dita dopo il fornetto.
    /// </summary>
    public static Bitmap PerCartaARilievo(Image immagine, float larghezzaMm, float altezzaMm, OpzioniGrafica opzioni, IReadOnlyList<string>? didascalia)
    {
        const int Risoluzione = 10; // pixel per millimetro, circa 254 dpi
        int lp = (int)(larghezzaMm * Risoluzione), ap = (int)(altezzaMm * Risoluzione);
        float alto = didascalia is { Count: > 0 } ? (float)(didascalia.Count * PassoRiga + PassoRiga / 2) : 0;

        // L'immagine nello spazio sotto la didascalia, il più grande possibile senza deformarla.
        float spazioMm = altezzaMm - alto;
        float scala = Math.Min(larghezzaMm / immagine.Width, spazioMm / immagine.Height);
        var area = new RectangleF((larghezzaMm - immagine.Width * scala) / 2, alto, immagine.Width * scala, immagine.Height * scala);
        var maschera = Maschera(immagine, lp, ap, Scala(area, Risoluzione), opzioni, spessore: (int)(1.5 * Risoluzione));

        var foglio = new Bitmap(lp, ap, PixelFormat.Format24bppRgb);
        foglio.SetResolution(254, 254);
        var dati = foglio.LockBits(new Rectangle(0, 0, lp, ap), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var riga = new byte[dati.Stride];
        for (int y = 0; y < ap; y++)
        {
            for (int x = 0; x < lp; x++)
            {
                byte v = maschera[y, x] ? (byte)0 : (byte)255;
                riga[x * 3] = riga[x * 3 + 1] = riga[x * 3 + 2] = v;
            }
            Marshal.Copy(riga, 0, dati.Scan0 + y * dati.Stride, dati.Stride);
        }
        foglio.UnlockBits(dati);

        if (didascalia is { Count: > 0 })
        {
            using var g = Graphics.FromImage(foglio);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float diametro = 1.6f * Risoluzione;
            for (int r = 0; r < didascalia.Count; r++)
                for (int c = 0; c < didascalia[r].Length; c++)
                {
                    int punti = didascalia[r][c] - TraduttoreBraille.CellaVuota;
                    for (int p = 0; p < 6; p++)
                        if ((punti & (1 << p)) != 0)
                        {
                            float x = (float)((c * PassoCella + (p / 3) * PassoPunto) * Risoluzione);
                            float y = (float)((r * PassoRiga + (p % 3) * PassoPunto) * Risoluzione);
                            g.FillEllipse(Brushes.Black, x, y, diametro, diametro);
                        }
                }
        }
        return foglio;
    }

    private static RectangleF Scala(RectangleF r, float fattore) => new(r.X * fattore, r.Y * fattore, r.Width * fattore, r.Height * fattore);

    /// <summary>
    /// Dove va il rilievo, pixel per pixel: ciò che si stacca dallo sfondo più della soglia
    /// (scelta da sola se non data), poi, per i contorni, i bordi delle figure ingrossati fino allo spessore.
    /// </summary>
    public static bool[,] Maschera(Image immagine, int larghezza, int altezza, RectangleF area, OpzioniGrafica opzioni, int spessore)
    {
        var simile = Somiglianza(immagine, larghezza, altezza, area);
        int soglia = opzioni.Soglia is > 0 and < 255 ? opzioni.Soglia : Otsu(simile, area);
        var nero = new bool[altezza, larghezza];
        for (int y = 0; y < altezza; y++)
            for (int x = 0; x < larghezza; x++)
                nero[y, x] = area.Contains(x + 0.5f, y + 0.5f) && (simile[y, x] < soglia) != opzioni.Inverti;
        if (opzioni.Tratto == TrattoTattile.Riempito)
            return nero;

        var bordi = new bool[altezza, larghezza];
        for (int y = 0; y < altezza; y++)
            for (int x = 0; x < larghezza; x++)
                bordi[y, x] = nero[y, x] && (x == 0 || y == 0 || x == larghezza - 1 || y == altezza - 1
                    || !nero[y, x - 1] || !nero[y, x + 1] || !nero[y - 1, x] || !nero[y + 1, x]);
        return Ingrossa(bordi, Math.Max(0, spessore / 2));
    }

    /// <summary>Ogni pixel acceso accende anche quelli nel quadrato di lato 2r+1 attorno (in due passate, veloce).</summary>
    private static bool[,] Ingrossa(bool[,] m, int r)
    {
        if (r == 0)
            return m;
        int h = m.GetLength(0), w = m.GetLength(1);
        var orizzontale = new bool[h, w];
        for (int y = 0; y < h; y++)
        {
            int ultimo = int.MinValue / 2;
            for (int x = 0; x < w + r; x++)
            {
                if (x < w && m[y, x])
                    ultimo = x;
                int c = x - r;
                if (c >= 0 && x - ultimo <= 2 * r)
                    orizzontale[y, c] = true;
            }
        }
        var risultato = new bool[h, w];
        for (int x = 0; x < w; x++)
        {
            int ultimo = int.MinValue / 2;
            for (int y = 0; y < h + r; y++)
            {
                if (y < h && orizzontale[y, x])
                    ultimo = y;
                int c = y - r;
                if (c >= 0 && y - ultimo <= 2 * r)
                    risultato[c, x] = true;
            }
        }
        return risultato;
    }

    /// <summary>
    /// Quanto ogni pixel somiglia allo sfondo: 255 uguale, 0 lontanissimo. Lo sfondo è il colore che sta di più
    /// sul bordo dell'immagine (la mediana dei pixel del bordo), così una figura gialla su bianco o un disegno
    /// bianco su nero vanno in rilievo come una figura nera su bianco. Fuori dall'immagine è tutto sfondo.
    /// </summary>
    private static byte[,] Somiglianza(Image immagine, int larghezza, int altezza, RectangleF area)
    {
        using var tela = new Bitmap(larghezza, altezza, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(tela))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(immagine, area);
        }
        var colori = new byte[altezza, larghezza * 3];
        var dati = tela.LockBits(new Rectangle(0, 0, larghezza, altezza), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        var riga = new byte[dati.Stride];
        for (int y = 0; y < altezza; y++)
        {
            Marshal.Copy(dati.Scan0 + y * dati.Stride, riga, 0, dati.Stride);
            Buffer.BlockCopy(riga, 0, colori, y * larghezza * 3, larghezza * 3);
        }
        tela.UnlockBits(dati);

        int x0 = Math.Clamp((int)Math.Ceiling(area.Left), 0, larghezza - 1), x1 = Math.Clamp((int)area.Right - 1, x0, larghezza - 1);
        int y0 = Math.Clamp((int)Math.Ceiling(area.Top), 0, altezza - 1), y1 = Math.Clamp((int)area.Bottom - 1, y0, altezza - 1);
        var bordo = new List<(int X, int Y)>();
        for (int x = x0; x <= x1; x++)
            bordo.AddRange([(x, y0), (x, y1)]);
        for (int y = y0; y <= y1; y++)
            bordo.AddRange([(x0, y), (x1, y)]);
        byte Mediana(int canale)
        {
            var valori = bordo.Select(p => colori[p.Y, p.X * 3 + canale]).Order().ToList();
            return valori[valori.Count / 2];
        }
        byte b = Mediana(0), v = Mediana(1), r = Mediana(2);

        var somiglianza = new byte[altezza, larghezza];
        for (int y = 0; y < altezza; y++)
            for (int x = 0; x < larghezza; x++)
            {
                int distanza = Math.Max(Math.Abs(colori[y, x * 3] - b), Math.Max(Math.Abs(colori[y, x * 3 + 1] - v), Math.Abs(colori[y, x * 3 + 2] - r)));
                somiglianza[y, x] = (byte)(255 - distanza);
            }
        return somiglianza;
    }

    /// <summary>La soglia di Otsu: quella che separa meglio figura e sfondo, calcolata solo dentro l'immagine.</summary>
    public static int Otsu(byte[,] luce, RectangleF area)
    {
        var istogramma = new long[256];
        long totale = 0;
        for (int y = Math.Max(0, (int)area.Top); y < Math.Min(luce.GetLength(0), (int)Math.Ceiling(area.Bottom)); y++)
            for (int x = Math.Max(0, (int)area.Left); x < Math.Min(luce.GetLength(1), (int)Math.Ceiling(area.Right)); x++)
            {
                istogramma[luce[y, x]]++;
                totale++;
            }
        if (totale == 0)
            return 128;
        double somma = 0;
        for (int i = 0; i < 256; i++)
            somma += i * (double)istogramma[i];
        double sommaScuri = 0, migliore = -1;
        long scuri = 0;
        int soglia = 128;
        for (int t = 0; t < 256; t++)
        {
            scuri += istogramma[t];
            if (scuri == 0)
                continue;
            long chiari = totale - scuri;
            if (chiari == 0)
                break;
            sommaScuri += t * (double)istogramma[t];
            double mediaScuri = sommaScuri / scuri, mediaChiari = (somma - sommaScuri) / chiari;
            double varianza = (double)scuri * chiari * (mediaScuri - mediaChiari) * (mediaScuri - mediaChiari);
            if (varianza > migliore)
            {
                migliore = varianza;
                soglia = t + 1;
            }
        }
        return soglia;
    }
}
