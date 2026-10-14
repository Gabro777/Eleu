using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Eleu.Documents;

/// <summary>
/// A document image: the data (PNG or JPEG), the displayed size, in points, and the description
/// the screen reader reads (Word's "alternative text").
/// </summary>
public sealed record EmbeddedImage(byte[] Data, string Format, float Width, float Height, string Description)
{
    public string Measures => $"{Width * 2.54f / 72f:0.#} per {Height * 2.54f / 72f:0.#} centimetri";
}

/// <summary>
/// Images in files and in the text box. As for bookmarks and footnotes, during loading and saving
/// each image is a placeholder character (Unicode private use area, from U+F200); in the open document it is
/// the real image, which the text box shows and prints by itself.
/// </summary>
public static partial class Images
{
    private const int First = 0xF200;
    public const int Max = 1024; // from U+F200 to U+F5FF; comments come after

    /// <summary>Maximum width of a newly inserted image: 15 cm, in points.</summary>
    public const float MaxWidth = 15 * 72f / 2.54f;

    public static char Placeholder(int index) => (char)(First + index);

    public static int Index(char character) =>
        character >= First && character < First + Max ? character - First : -1;

    /// <summary>
    /// An image from a file or from any data: PNG and JPEG stay as they are, other formats
    /// (BMP, GIF, TIFF, ...) become PNG. The size is the natural one, reduced if wider than 15 cm.
    /// </summary>
    public static EmbeddedImage FromData(byte[] data, string description, float? desiredWidth = null)
    {
        using var stream = new MemoryStream(data);
        using var bitmap = System.Drawing.Image.FromStream(stream);
        string format;
        if (bitmap.RawFormat.Guid == ImageFormat.Png.Guid)
            format = "png";
        else if (bitmap.RawFormat.Guid == ImageFormat.Jpeg.Guid)
            format = "jpeg";
        else
        {
            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            data = png.ToArray();
            format = "png";
        }
        // PNG stores the resolution in dots per meter: 96 dpi comes back as 95.98. Round it.
        float dpiX = bitmap.HorizontalResolution > 1 ? MathF.Round(bitmap.HorizontalResolution) : 96;
        float dpiY = bitmap.VerticalResolution > 1 ? MathF.Round(bitmap.VerticalResolution) : 96;
        float width = bitmap.Width * 72f / dpiX, height = bitmap.Height * 72f / dpiY;
        float desired = desiredWidth ?? Math.Min(width, MaxWidth);
        return new EmbeddedImage(data, format, desired, height * desired / width, description);
    }

    /// <summary>The same with another width; the height follows, so it is not distorted.</summary>
    public static EmbeddedImage WithWidth(EmbeddedImage image, float width) =>
        image with { Width = width, Height = image.Height * width / image.Width };

    /// <summary>
    /// The image in RTF, as Word writes it: hex data, size in twips and the description
    /// in wzDescription (inside \picprop, which the text box ignores but Word reads).
    /// </summary>
    public static string Pict(EmbeddedImage image)
    {
        using var stream = new MemoryStream(image.Data);
        using var bitmap = System.Drawing.Image.FromStream(stream);
        var rtf = new StringBuilder("{");
        rtf.Append(@"\pict{\*\picprop{\sp{\sn wzDescription}{\sv ");
        Escape(image.Description, rtf);
        rtf.Append("}}}");
        rtf.Append(image.Format == "jpeg" ? @"\jpegblip" : @"\pngblip");
        rtf.Append(@"\picw").Append(bitmap.Width).Append(@"\pich").Append(bitmap.Height);
        rtf.Append(@"\picwgoal").Append(RtfWriter.Twip(image.Width)).Append(@"\pichgoal").Append(RtfWriter.Twip(image.Height));
        rtf.Append(' ');
        for (int i = 0; i < image.Data.Length; i++)
        {
            rtf.Append(image.Data[i].ToString("x2", CultureInfo.InvariantCulture));
            if (i % 64 == 63)
                rtf.Append("\r\n");
        }
        return rtf.Append('}').ToString();
    }

    /// <summary>An RTF document containing only the image, to insert into the text box.</summary>
    public static string RtfImageOnly(EmbeddedImage image) => "{" + @"\rtf1\ansi " + Pict(image) + "}";

    /// <summary>
    /// RTF of a file → same RTF with a placeholder in place of each image; the images go into
    /// <paramref name="images"/>. Word often sends the same image twice, {\*\shppict} and
    /// {\nonshppict} (fallback for old programs): the first is kept and the fallback discarded.
    /// Images in formats that cannot be read stay as they are.
    /// </summary>
    public static string ToPlaceholders(string rtf, List<EmbeddedImage> images)
    {
        var result = new StringBuilder();
        int from = 0;
        foreach (Match m in ImageGroup().Matches(rtf))
        {
            if (m.Index < from)
                continue;
            int end = GroupEnd(rtf, m.Index);
            if (end < 0)
                break;
            var group = rtf[m.Index..(end + 1)];
            result.Append(rtf, from, m.Index - from);
            from = end + 1;
            if (group.StartsWith(@"{\nonshppict", StringComparison.Ordinal))
                continue; // the fallback
            if (images.Count < Max && Read(group) is { } image)
            {
                images.Add(image);
                result.Append('{').Append(RtfCharacter(Placeholder(images.Count - 1))).Append('}');
            }
            else
            {
                result.Append(group);
            }
        }
        result.Append(rtf, from, rtf.Length - from);
        return result.ToString();
    }

    /// <summary>RTF with placeholders → RTF with the images.</summary>
    public static string FromPlaceholders(string rtf, IReadOnlyList<EmbeddedImage> images) =>
        UnicodeRtf().Replace(rtf, m =>
        {
            int index = Index((char)(ushort)short.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            return index >= 0 && index < images.Count ? Pict(images[index]) : m.Value;
        });

    /// <summary>A {\pict ...} group (also inside {\*\shppict}) as an image, or null if the format cannot be read.</summary>
    private static EmbeddedImage? Read(string group)
    {
        int pictStart = group.IndexOf(@"\pict", StringComparison.Ordinal);
        if (pictStart < 0)
            return null;
        var description = Description().Match(group) is { Success: true } d ? RtfText(d.Groups[1].Value) : "";
        // The hex data is what remains after removing inner groups and control words.
        var body = InnerGroups().Replace(group[(pictStart + 5)..], "");
        var words = ControlWords().Matches(body).ToDictionary(p => p.Groups[1].Value, p => p.Groups[2].Value, StringComparer.Ordinal);
        var hex = new StringBuilder();
        foreach (var c in ControlWords().Replace(body, ""))
            if (Uri.IsHexDigit(c))
                hex.Append(c);
        if (hex.Length < 2)
            return null;
        byte[] data;
        try
        {
            data = Convert.FromHexString(hex.Length % 2 == 0 ? hex.ToString() : hex.ToString(0, hex.Length - 1));
        }
        catch (FormatException)
        {
            return null;
        }

        float? width = words.TryGetValue("picwgoal", out var w) && int.TryParse(w, out var tw) && tw > 0 ? tw / 20f : null;
        float scale = words.TryGetValue("picscalex", out var sx) && int.TryParse(sx, out var s) && s > 0 ? s / 100f : 1f;
        try
        {
            var image = FromData(data, description, width is float l ? l * scale : null);
            return image;
        }
        catch (Exception e) when (e is ArgumentException or ExternalException or OutOfMemoryException)
        {
            return null; // format System.Drawing cannot read (for example WMF without a header)
        }
    }

    private static int GroupEnd(string rtf, int opening)
    {
        int level = 0;
        for (int i = opening; i < rtf.Length; i++)
        {
            switch (rtf[i])
            {
                case '\\':
                    i++;
                    break;
                case '{':
                    level++;
                    break;
                case '}':
                    if (--level == 0)
                        return i;
                    break;
            }
        }
        return -1;
    }

    private static string RtfCharacter(char c) => $@"\uc1\u{(short)c}?";

    private static void Escape(string text, StringBuilder rtf)
    {
        foreach (var c in text)
        {
            if (c is '\\' or '{' or '}')
                rtf.Append('\\').Append(c);
            else if (c > 127)
                rtf.Append(RtfCharacter(c));
            else if (c >= 32)
                rtf.Append(c);
            else
                rtf.Append(' ');
        }
    }

    private static string RtfText(string rtf) =>
        RtfCode().Replace(UnicodeSkip().Replace(rtf, ""), m => m.Groups[1].Success
            ? ((char)byte.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString()
            : m.Groups[2].Success
                ? ((char)(ushort)short.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToString()
                : m.Groups[3].Value).Trim();

    [GeneratedRegex(@"\{\\(?:\*\\shppict|nonshppict|pict)(?![a-z])")]
    private static partial Regex ImageGroup();

    [GeneratedRegex(@"\{\\sn wzDescription\}\{\\sv ?((?:[^{}\\]|\\.)*)\}")]
    private static partial Regex Description();

    [GeneratedRegex(@"\{[^{}]*(?:\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}[^{}]*)*\}")]
    private static partial Regex InnerGroups();

    [GeneratedRegex(@"\\([a-z]+)(-?\d*) ?")]
    private static partial Regex ControlWords();

    [GeneratedRegex(@"\\u(-?\d+) ?(?:\\'[0-9a-fA-F]{2}|\?)?")]
    private static partial Regex UnicodeRtf();

    [GeneratedRegex(@"\\'([0-9a-fA-F]{2})|\\u(-?\d+)\??|\\([\\{}])")]
    private static partial Regex RtfCode();

    [GeneratedRegex(@"\\uc\d+ ?")]
    private static partial Regex UnicodeSkip();
}
