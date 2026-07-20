using System.Globalization;
using System.Text;
using iText.Kernel.Colors;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Matrix = iText.Kernel.Geom.Matrix;
using Rectangle = iText.Kernel.Geom.Rectangle;
using GestionAtelier.Models;

namespace GestionAtelier.Services;

public static class PdfAnalyzer
{
    private const double PointToMillimeter = 25.4d / 72d;
    private static readonly HashSet<string> Standard14Fonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique",
        "Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats"
    };

    public static PdfAnalysisReport Analyze(string fullPath, string? allowedRootPath = null)
    {
        var report = new PdfAnalysisReport();

        try
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return BuildError("Chemin PDF vide.");

            var absolutePath = Path.GetFullPath(fullPath);
            var effectiveRoot = ResolveAllowedRoot(allowedRootPath);
            if (!string.IsNullOrWhiteSpace(effectiveRoot) && !IsPathInsideRoot(absolutePath, effectiveRoot))
                return BuildError("Chemin PDF hors périmètre autorisé.");

            if (!File.Exists(absolutePath))
                return BuildError("Fichier PDF introuvable.");

            using var reader = new PdfReader(absolutePath);
            using var pdf = new PdfDocument(reader);

            report.PageCount = pdf.GetNumberOfPages();
            if (report.PageCount == 0)
                return report;

            var embeddedFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var subsetFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var spotColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasOverprint = false;

            for (var pageIndex = 1; pageIndex <= report.PageCount; pageIndex++)
            {
                var page = pdf.GetPage(pageIndex);
                var pageObject = page.GetPdfObject();

                if (pageIndex == 1)
                {
                    report.MediaBox = BuildBoxMetrics(GetBoxArray(pageObject, PdfName.MediaBox), page.GetMediaBox());
                    report.TrimBox = BuildBoxMetrics(GetBoxArray(pageObject, PdfName.TrimBox), page.GetTrimBox());
                    report.BleedBox = BuildBoxMetrics(GetBoxArray(pageObject, PdfName.BleedBox), page.GetBleedBox());
                    report.CropBox = BuildBoxMetrics(GetBoxArray(pageObject, PdfName.CropBox), page.GetCropBox());

                    if (report.TrimBox.Present && report.TrimBox.WidthMm.HasValue && report.TrimBox.HeightMm.HasValue)
                        report.FinishedFormat = DetectFormat(report.TrimBox.WidthMm.Value, report.TrimBox.HeightMm.Value);

                    report.BleedMm = ComputeBleedMm(report.TrimBox, report.BleedBox);
                }

                var resources = pageObject.GetAsDictionary(PdfName.Resources);
                if (resources != null)
                {
                    AnalyzeColorSpaces(resources.GetAsDictionary(PdfName.ColorSpace), report, spotColors);
                    AnalyzeFonts(resources.GetAsDictionary(PdfName.Font), embeddedFonts, subsetFonts, missingFonts);
                    hasOverprint |= AnalyzeExtGStateForOverprint(resources.GetAsDictionary(PdfName.ExtGState));
                }

                var listener = new PdfAnalysisEventListener();
                var processor = new PdfCanvasProcessor(listener);
                processor.ProcessPageContent(page);

                report.UsesRgb |= listener.UsesRgb;
                report.UsesCmyk |= listener.UsesCmyk;
                report.UsesGray |= listener.UsesGray;

                foreach (var color in listener.SpotColors)
                    spotColors.Add(color);

                if (listener.MinImageDpi.HasValue)
                    report.MinImageDpi = UpdateMinimum(report.MinImageDpi, listener.MinImageDpi.Value);

                report.ImagesBelow300DpiCount += listener.ImagesBelow300DpiCount;
            }

            report.SpotColors = spotColors.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

            report.EmbeddedFonts = embeddedFonts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            report.SubsetFonts = subsetFonts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            report.MissingFonts = missingFonts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            report.HasEmbeddedFonts = report.EmbeddedFonts.Count > 0;
            report.HasSubsetFonts = report.SubsetFonts.Count > 0;
            report.HasMissingFonts = report.MissingFonts.Count > 0;

            if (report.MinImageDpi.HasValue)
                report.MinImageDpi = Math.Round(report.MinImageDpi.Value, 2);

            report.HasOverprint = hasOverprint;

            return report;
        }
        catch (Exception ex)
        {
            return BuildError($"Impossible d'analyser le PDF: {ex.Message}");
        }
    }

    private static PdfAnalysisReport BuildError(string message)
        => new()
        {
            IsError = true,
            ErrorMessage = message
        };

    private static PdfArray? GetBoxArray(PdfDictionary? pageDict, PdfName boxName)
    {
        var current = pageDict;
        while (current != null)
        {
            var box = current.GetAsArray(boxName);
            if (box != null) return box;
            current = current.GetAsDictionary(PdfName.Parent);
        }

        return null;
    }

    private static PdfBoxMetrics BuildBoxMetrics(PdfArray? boxArray, Rectangle? fallback)
    {
        if (boxArray != null && boxArray.Size() >= 4)
        {
            var left = boxArray.GetAsNumber(0)?.DoubleValue();
            var bottom = boxArray.GetAsNumber(1)?.DoubleValue();
            var right = boxArray.GetAsNumber(2)?.DoubleValue();
            var top = boxArray.GetAsNumber(3)?.DoubleValue();

            if (left.HasValue && bottom.HasValue && right.HasValue && top.HasValue)
            {
                var widthPt = Math.Max(0d, right.Value - left.Value);
                var heightPt = Math.Max(0d, top.Value - bottom.Value);
                return new PdfBoxMetrics
                {
                    Present = true,
                    LeftPt = left.Value,
                    BottomPt = bottom.Value,
                    RightPt = right.Value,
                    TopPt = top.Value,
                    WidthPt = Math.Round(widthPt, 2),
                    HeightPt = Math.Round(heightPt, 2),
                    WidthMm = Math.Round(widthPt * PointToMillimeter, 2),
                    HeightMm = Math.Round(heightPt * PointToMillimeter, 2)
                };
            }
        }

        if (fallback != null)
        {
            var widthPt = Math.Max(0d, fallback.GetWidth());
            var heightPt = Math.Max(0d, fallback.GetHeight());
            return new PdfBoxMetrics
            {
                Present = true,
                LeftPt = Math.Round(fallback.GetLeft(), 2),
                BottomPt = Math.Round(fallback.GetBottom(), 2),
                RightPt = Math.Round(fallback.GetRight(), 2),
                TopPt = Math.Round(fallback.GetTop(), 2),
                WidthPt = Math.Round(widthPt, 2),
                HeightPt = Math.Round(heightPt, 2),
                WidthMm = Math.Round(widthPt * PointToMillimeter, 2),
                HeightMm = Math.Round(heightPt * PointToMillimeter, 2)
            };
        }

        return new PdfBoxMetrics { Present = false };
    }

    private static double? ComputeBleedMm(PdfBoxMetrics trimBox, PdfBoxMetrics bleedBox)
    {
        if (!trimBox.Present || !bleedBox.Present)
            return null;

        if (!trimBox.LeftPt.HasValue || !trimBox.BottomPt.HasValue || !trimBox.RightPt.HasValue || !trimBox.TopPt.HasValue)
            return null;

        if (!bleedBox.LeftPt.HasValue || !bleedBox.BottomPt.HasValue || !bleedBox.RightPt.HasValue || !bleedBox.TopPt.HasValue)
            return null;

        var left = Math.Max(0d, trimBox.LeftPt.Value - bleedBox.LeftPt.Value) * PointToMillimeter;
        var bottom = Math.Max(0d, trimBox.BottomPt.Value - bleedBox.BottomPt.Value) * PointToMillimeter;
        var right = Math.Max(0d, bleedBox.RightPt.Value - trimBox.RightPt.Value) * PointToMillimeter;
        var top = Math.Max(0d, bleedBox.TopPt.Value - trimBox.TopPt.Value) * PointToMillimeter;

        return Math.Round(new[] { left, bottom, right, top }.Min(), 2);
    }

    private static string DetectFormat(double widthMm, double heightMm)
    {
        var candidates = new Dictionary<string, (double W, double H)>
        {
            ["A0"] = (841, 1189),
            ["A1"] = (594, 841),
            ["A2"] = (420, 594),
            ["A3"] = (297, 420),
            ["A4"] = (210, 297),
            ["A5"] = (148, 210),
            ["A6"] = (105, 148)
        };

        var w = Math.Min(widthMm, heightMm);
        var h = Math.Max(widthMm, heightMm);
        // Tolérance pragmatique pour accepter les petites variations d'export (arrondis/boîtes PDF).
        const double tolerance = 2.0d;

        foreach (var (name, dims) in candidates)
        {
            var cw = Math.Min(dims.W, dims.H);
            var ch = Math.Max(dims.W, dims.H);
            if (Math.Abs(w - cw) <= tolerance && Math.Abs(h - ch) <= tolerance)
                return name;
        }

        return $"{widthMm.ToString("0.#", CultureInfo.InvariantCulture)}x{heightMm.ToString("0.#", CultureInfo.InvariantCulture)} mm";
    }

    private static void AnalyzeColorSpaces(PdfDictionary? colorSpaceDict, PdfAnalysisReport report, HashSet<string> spotColors)
    {
        if (colorSpaceDict == null) return;
        foreach (var entry in colorSpaceDict.EntrySet())
            AnalyzeColorSpaceObject(entry.Value, report, spotColors);
    }

    private static void AnalyzeColorSpaceObject(PdfObject? obj, PdfAnalysisReport report, HashSet<string> spotColors)
    {
        if (obj == null) return;

        if (obj is PdfName name)
        {
            SetColorFlagsByName(name, report);
            return;
        }

        if (obj is PdfArray array)
        {
            if (array.Size() > 0 && array.Get(0) is PdfName firstName)
            {
                SetColorFlagsByName(firstName, report);

                if (firstName.Equals(PdfName.Separation))
                {
                    if (array.Size() > 1 && array.Get(1) is PdfName spotName)
                    {
                        var s = DecodePdfName(spotName);
                        if (!string.IsNullOrWhiteSpace(s))
                            spotColors.Add(s);
                    }
                }
                else if (firstName.Equals(PdfName.DeviceN))
                {
                    if (array.Size() > 1 && array.Get(1) is PdfArray namesArray)
                    {
                        foreach (var entry in namesArray)
                        {
                            if (entry is PdfName channelName)
                            {
                                var channel = DecodePdfName(channelName);
                                if (IsCmykProcessChannel(channel))
                                    continue;

                                if (!string.IsNullOrWhiteSpace(channel))
                                    spotColors.Add(channel);
                            }
                        }
                    }
                }
            }

            foreach (var item in array)
                AnalyzeColorSpaceObject(item, report, spotColors);

            return;
        }

        if (obj is PdfDictionary dict)
        {
            foreach (var entry in dict.EntrySet())
                AnalyzeColorSpaceObject(entry.Value, report, spotColors);
        }
    }

    private static void SetColorFlagsByName(PdfName name, PdfAnalysisReport report)
    {
        if (name.Equals(PdfName.DeviceRGB) || name.Equals(PdfName.CalRGB))
        {
            report.UsesRgb = true;
            return;
        }

        if (name.Equals(PdfName.DeviceCMYK))
        {
            report.UsesCmyk = true;
            return;
        }

        if (name.Equals(PdfName.DeviceGray) || name.Equals(PdfName.CalGray))
        {
            report.UsesGray = true;
        }
    }

    private static void AnalyzeFonts(
        PdfDictionary? fontsDict,
        HashSet<string> embeddedFonts,
        HashSet<string> subsetFonts,
        HashSet<string> missingFonts)
    {
        if (fontsDict == null) return;

        foreach (var entry in fontsDict.EntrySet())
        {
            var fontDict = entry.Value as PdfDictionary;
            if (fontDict == null) continue;

            var baseFontName = DecodePdfName(fontDict.GetAsName(PdfName.BaseFont));
            if (string.IsNullOrWhiteSpace(baseFontName))
                baseFontName = DecodePdfName(entry.Key);

            var normalizedBaseFont = NormalizeFontName(baseFontName);
            var isSubset = baseFontName.Contains('+');

            if (isSubset)
                subsetFonts.Add(normalizedBaseFont);

            var descriptor = fontDict.GetAsDictionary(PdfName.FontDescriptor);
            var isEmbedded = descriptor != null &&
                             (descriptor.ContainsKey(PdfName.FontFile) ||
                              descriptor.ContainsKey(PdfName.FontFile2) ||
                              descriptor.ContainsKey(PdfName.FontFile3));

            if (isEmbedded)
                embeddedFonts.Add(normalizedBaseFont);
            else if (!Standard14Fonts.Contains(normalizedBaseFont))
                missingFonts.Add(normalizedBaseFont);
        }
    }

    /// <summary>
    /// Vérifie si l'un des états graphiques (ExtGState) du dictionnaire de ressources active
    /// le mode surimpression (overprint). La détection repose sur les clés <c>OP</c> (stroke) et
    /// <c>op</c> (fill) de la spécification PDF (Table 58 de la spec ISO 32000).
    /// </summary>
    private static bool AnalyzeExtGStateForOverprint(PdfDictionary? extGStateDict)
    {
        if (extGStateDict == null) return false;

        foreach (var entry in extGStateDict.EntrySet())
        {
            if (entry.Value is not PdfDictionary stateDict) continue;

            // OP (uppercase) = surimpression pour les opérations de remplissage (stroke overprint)
            if (stateDict.Get(PdfName.OP) is PdfBoolean opVal && opVal.GetValue())
                return true;
            // op (lowercase) = surimpression pour les opérations non-stroke (fill overprint)
            if (stateDict.Get(new PdfName("op")) is PdfBoolean opFillVal && opFillVal.GetValue())
                return true;
        }

        return false;
    }

    private static string DecodePdfName(PdfName? name)    {
        var raw = name?.ToString() ?? "";
        if (raw.StartsWith('/')) raw = raw[1..];

        if (!raw.Contains('#'))
            return raw;

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '#' && i + 2 < raw.Length &&
                byte.TryParse(raw.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                sb.Append((char)value);
                i += 2;
                continue;
            }

            sb.Append(raw[i]);
        }

        return sb.ToString();
    }

    private static string NormalizeFontName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "";
        var idx = rawName.IndexOf('+');
        return idx >= 0 && idx + 1 < rawName.Length ? rawName[(idx + 1)..] : rawName;
    }

    private static double UpdateMinimum(double? currentMinimum, double candidate)
        => currentMinimum.HasValue ? Math.Min(currentMinimum.Value, candidate) : candidate;

    private static bool IsCmykProcessChannel(string channel)
        => channel.Equals("Cyan", StringComparison.OrdinalIgnoreCase) ||
           channel.Equals("Magenta", StringComparison.OrdinalIgnoreCase) ||
           channel.Equals("Yellow", StringComparison.OrdinalIgnoreCase) ||
           channel.Equals("Black", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveAllowedRoot(string? providedRootPath)
    {
        if (!string.IsNullOrWhiteSpace(providedRootPath))
            return Path.GetFullPath(providedRootPath);

        try
        {
            var hotfolderRoot = BackendUtils.HotfoldersRoot();
            return string.IsNullOrWhiteSpace(hotfolderRoot) ? null : Path.GetFullPath(hotfolderRoot);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsPathInsideRoot(string absolutePath, string absoluteRootPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var normalizedRoot = Path.TrimEndingDirectorySeparator(absoluteRootPath);
        var normalizedPath = Path.GetFullPath(absolutePath);

        if (string.Equals(normalizedPath, normalizedRoot, comparison))
            return true;

        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(rootPrefix, comparison);
    }

    private sealed class PdfAnalysisEventListener : IEventListener
    {
        public bool UsesRgb { get; private set; } = false;
        public bool UsesCmyk { get; private set; } = false;
        public bool UsesGray { get; private set; } = false;
        public HashSet<string> SpotColors { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double? MinImageDpi { get; private set; }
        public int ImagesBelow300DpiCount { get; private set; } = 0;

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT && data is TextRenderInfo textInfo)
            {
                DetectColor(textInfo.GetFillColor());
                DetectColor(textInfo.GetStrokeColor());
                return;
            }

            if (type == EventType.RENDER_IMAGE && data is ImageRenderInfo imageInfo)
            {
                var image = imageInfo.GetImage();
                if (image == null) return;

                var imageObject = image.GetPdfObject();
                DetectColorSpace(imageObject.Get(PdfName.ColorSpace));

                var matrix = imageInfo.GetImageCtm();
                var a = matrix.Get(Matrix.I11);
                var b = matrix.Get(Matrix.I12);
                var c = matrix.Get(Matrix.I21);
                var d = matrix.Get(Matrix.I22);
                var widthPt = Math.Sqrt((a * a) + (b * b));
                var heightPt = Math.Sqrt((c * c) + (d * d));

                if (widthPt <= 0 || heightPt <= 0) return;

                var dpiX = image.GetWidth() * 72d / widthPt;
                var dpiY = image.GetHeight() * 72d / heightPt;
                var minDpi = Math.Min(dpiX, dpiY);

                MinImageDpi = UpdateMinimum(MinImageDpi, minDpi);

                if (minDpi < 300d)
                    ImagesBelow300DpiCount++;
            }
        }

        public ICollection<EventType>? GetSupportedEvents() => null;

        private void DetectColor(Color? color)
        {
            if (color == null) return;
            var pdfObject = color.GetColorSpace()?.GetPdfObject();
            DetectColorSpace(pdfObject);
        }

        private void DetectColorSpace(PdfObject? obj)
            => AnalyzeColorSpaceObject(obj, this);

        private static void AnalyzeColorSpaceObject(PdfObject? obj, PdfAnalysisEventListener listener)
        {
            if (obj == null) return;

            if (obj is PdfName name)
            {
                if (name.Equals(PdfName.DeviceRGB) || name.Equals(PdfName.CalRGB))
                    listener.UsesRgb = true;
                else if (name.Equals(PdfName.DeviceCMYK))
                    listener.UsesCmyk = true;
                else if (name.Equals(PdfName.DeviceGray) || name.Equals(PdfName.CalGray))
                    listener.UsesGray = true;

                return;
            }

            if (obj is PdfArray array)
            {
                if (array.Size() > 0 && array.Get(0) is PdfName firstName)
                {
                    AnalyzeColorSpaceObject(firstName, listener);

                    if (firstName.Equals(PdfName.Separation) && array.Size() > 1 && array.Get(1) is PdfName separationName)
                    {
                        var s = DecodePdfName(separationName);
                        if (!string.IsNullOrWhiteSpace(s))
                            listener.SpotColors.Add(s);
                    }
                    else if (firstName.Equals(PdfName.DeviceN) && array.Size() > 1 && array.Get(1) is PdfArray namesArray)
                    {
                        foreach (var entry in namesArray)
                        {
                            if (entry is not PdfName channelName) continue;
                            var channel = DecodePdfName(channelName);
                            if (IsCmykProcessChannel(channel))
                                continue;

                            if (!string.IsNullOrWhiteSpace(channel))
                                listener.SpotColors.Add(channel);
                        }
                    }
                }

                foreach (var item in array)
                    AnalyzeColorSpaceObject(item, listener);

                return;
            }

            if (obj is PdfDictionary dict)
            {
                foreach (var entry in dict.EntrySet())
                    AnalyzeColorSpaceObject(entry.Value, listener);
            }
        }
    }
}
