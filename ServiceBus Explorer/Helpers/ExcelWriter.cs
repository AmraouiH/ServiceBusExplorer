using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace ServiceBusExplorer.Helpers
{
    /// <summary>
    /// Minimal .xlsx writer (Office Open XML zipped by hand): bold frozen header row, auto-filter,
    /// real dates and numbers. No third-party library, so the plugin stays a single DLL.
    /// </summary>
    internal class ExcelWriter
    {
        private const int MaxCellLength = 32000;

        private class Sheet
        {
            public string Name;
            public string[] Headers;
            public List<object[]> Rows;
            public double[] Widths;
        }

        private readonly List<Sheet> _sheets = new List<Sheet>();

        public void AddSheet(string name, string[] headers, IEnumerable<object[]> rows, double[] widths = null)
        {
            var safe = new string((name ?? "Sheet").Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray());
            if (safe.Length > 31) safe = safe.Substring(0, 31);
            var unique = safe;
            for (var i = 2; _sheets.Any(s => string.Equals(s.Name, unique, StringComparison.OrdinalIgnoreCase)); i++)
            {
                unique = (safe.Length > 28 ? safe.Substring(0, 28) : safe) + " " + i;
            }
            _sheets.Add(new Sheet { Name = unique, Headers = headers, Rows = rows.ToList(), Widths = widths });
        }

        public void Save(string path)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(zip, "[Content_Types].xml", ContentTypes());
                Write(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "</Relationships>");
                Write(zip, "xl/workbook.xml", Workbook());
                Write(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                Write(zip, "xl/styles.xml", Styles());
                for (var i = 0; i < _sheets.Count; i++)
                {
                    Write(zip, $"xl/worksheets/sheet{i + 1}.xml", Worksheet(_sheets[i]));
                }
            }
        }

        private static void Write(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private string ContentTypes()
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            builder.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            builder.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            builder.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            builder.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            builder.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            for (var i = 0; i < _sheets.Count; i++)
            {
                builder.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            builder.Append("</Types>");
            return builder.ToString();
        }

        private string Workbook()
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            builder.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            for (var i = 0; i < _sheets.Count; i++)
            {
                builder.Append($"<sheet name=\"{Escape(_sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            }
            builder.Append("</sheets><definedNames>");
            for (var i = 0; i < _sheets.Count; i++)
            {
                var sheet = _sheets[i];
                if (sheet.Headers.Length == 0) continue;
                builder.Append($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">'{Escape(sheet.Name.Replace("'", "''"))}'!$A$1:${Column(sheet.Headers.Length - 1)}${sheet.Rows.Count + 1}</definedName>");
            }
            builder.Append("</definedNames></workbook>");
            return builder.ToString();
        }

        private string WorkbookRels()
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            builder.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (var i = 0; i < _sheets.Count; i++)
            {
                builder.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
            }
            builder.Append($"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            builder.Append("</Relationships>");
            return builder.ToString();
        }

        /// <summary>Styles: 0 = default, 1 = header (bold, white on violet), 2 = date time, 3 = wrapped text, 4 = percentage.</summary>
        private static string Styles()
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd hh:mm\"/></numFmts>" +
                   "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Segoe UI\"/></font><font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Segoe UI\"/></font></fonts>" +
                   "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                   "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF5B4FE8\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                   "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
                   "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                   "<cellXfs count=\"5\">" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                   "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\"/></xf>" +
                   "<xf numFmtId=\"10\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
                   "</cellXfs>" +
                   "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
                   "</styleSheet>";
        }

        private static string Worksheet(Sheet sheet)
        {
            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            builder.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");

            builder.Append("<cols>");
            for (var c = 0; c < sheet.Headers.Length; c++)
            {
                var width = sheet.Widths != null && c < sheet.Widths.Length ? sheet.Widths[c] : EstimateWidth(sheet, c);
                builder.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width.ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            builder.Append("</cols><sheetData>");

            builder.Append("<row r=\"1\">");
            for (var c = 0; c < sheet.Headers.Length; c++)
            {
                AppendCell(builder, $"{Column(c)}1", sheet.Headers[c], 1);
            }
            builder.Append("</row>");

            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                var rowNumber = r + 2;
                builder.Append($"<row r=\"{rowNumber}\">");
                var row = sheet.Rows[r];
                for (var c = 0; c < row.Length && c < sheet.Headers.Length; c++)
                {
                    AppendCell(builder, $"{Column(c)}{rowNumber}", row[c], 0);
                }
                builder.Append("</row>");
            }
            builder.Append("</sheetData>");

            if (sheet.Headers.Length > 0)
            {
                builder.Append($"<autoFilter ref=\"A1:{Column(sheet.Headers.Length - 1)}{sheet.Rows.Count + 1}\"/>");
            }
            builder.Append("</worksheet>");
            return builder.ToString();
        }

        private static void AppendCell(StringBuilder builder, string reference, object value, int style)
        {
            switch (value)
            {
                case null:
                    return;
                case DateTime date:
                    builder.Append($"<c r=\"{reference}\" s=\"2\"><v>{date.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>");
                    return;
                case Percentage percentage:
                    builder.Append($"<c r=\"{reference}\" s=\"4\"><v>{percentage.Value.ToString(CultureInfo.InvariantCulture)}</v></c>");
                    return;
                case int _:
                case long _:
                case double _:
                case decimal _:
                case float _:
                    builder.Append($"<c r=\"{reference}\"{(style > 0 ? $" s=\"{style}\"" : string.Empty)}><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>");
                    return;
                default:
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (text.Length > MaxCellLength) text = text.Substring(0, MaxCellLength) + "…";
                    var cellStyle = style > 0 ? style : text.IndexOf('\n') >= 0 ? 3 : 0;
                    builder.Append($"<c r=\"{reference}\" t=\"inlineStr\"{(cellStyle > 0 ? $" s=\"{cellStyle}\"" : string.Empty)}><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>");
                    return;
            }
        }

        private static double EstimateWidth(Sheet sheet, int column)
        {
            var longest = sheet.Headers[column]?.Length ?? 8;
            foreach (var row in sheet.Rows.Take(300))
            {
                if (column >= row.Length || row[column] == null) continue;
                var length = row[column] is DateTime ? 16 : Convert.ToString(row[column], CultureInfo.InvariantCulture).Split('\n')[0].Length;
                longest = Math.Max(longest, length);
            }
            return Math.Min(80, Math.Max(10, longest * 1.1 + 2));
        }

        private static string Column(int index)
        {
            var name = string.Empty;
            for (var i = index + 1; i > 0; i = (i - 1) / 26)
            {
                name = (char)('A' + (i - 1) % 26) + name;
            }
            return name;
        }

        /// <summary>XML escaping that also drops the control characters forbidden in XML 1.0.</summary>
        private static string Escape(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '<': builder.Append("&lt;"); break;
                    case '>': builder.Append("&gt;"); break;
                    case '&': builder.Append("&amp;"); break;
                    case '"': builder.Append("&quot;"); break;
                    default:
                        if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF)) builder.Append(ch);
                        break;
                }
            }
            return builder.ToString();
        }

        /// <summary>A value written with the percentage format.</summary>
        public struct Percentage
        {
            public Percentage(double value) { Value = value; }
            public double Value { get; }
        }
    }
}
