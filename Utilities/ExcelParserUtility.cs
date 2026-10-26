using ClosedXML.Excel;
using MockServer.Models;
using System.Text.Json;

namespace MockServer.Utilities
{
    public static class ExcelParserUtility
    {
        public static List<Dictionary<string, object>> ParseExcelRows(IFormFile file, Dictionary<string, string> responseProperties)
        {
            var records = new List<Dictionary<string, object>>();
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet(1); // Read the first worksheet
            var rows = worksheet.RowsUsed().ToList();

            if (rows.Count < 2) return records; // Requires at least a header row + 1 data row

            // Map column index to property name based on header text matching
            var headerRow = rows[0];
            var columnMapping = new Dictionary<int, string>();

            foreach (var cell in headerRow.CellsUsed())
            {
                string headerName = cell.Value.ToString().Trim();
                string normHeader = NormalizeKey(headerName);

                var matchedProp = responseProperties.Keys
                    .FirstOrDefault(k => k.Equals(headerName, StringComparison.OrdinalIgnoreCase))
                    ?? responseProperties.Keys.FirstOrDefault(k => NormalizeKey(k).Equals(normHeader, StringComparison.OrdinalIgnoreCase));

                if (matchedProp != null)
                {
                    columnMapping[cell.Address.ColumnNumber] = matchedProp;
                }
            }

            // Parse data rows (skipping header)
            foreach (var row in rows.Skip(1))
            {
                var record = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var col in columnMapping)
                {
                    var cellValue = row.Cell(col.Key).Value;
                    string propType = responseProperties[col.Value];
                    var converted = ConvertCellType(cellValue, propType);

                    // Store both original property key and sanitized identifier key
                    record[col.Value] = converted;
                    string sanitizedKey = SanitizeKey(col.Value);
                    if (!record.ContainsKey(sanitizedKey))
                    {
                        record[sanitizedKey] = converted;
                    }
                }
                records.Add(record);
            }

            return records;
        }

        private static string NormalizeKey(string key) =>
            new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        private static string SanitizeKey(string key)
        {
            var clean = new string(key.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(clean)) return "_item";
            if (char.IsDigit(clean[0])) clean = "_" + clean;
            return clean;
        }

        private static object ConvertCellType(XLCellValue cellValue, string typeName)
        {
            if (cellValue.IsBlank) return null!;

            return typeName.ToLowerInvariant() switch
            {
                "int" or "integer" => cellValue.TryConvert(out double d, System.Globalization.CultureInfo.InvariantCulture) ? (int)d : 0,
                "long" => cellValue.TryConvert(out double l, System.Globalization.CultureInfo.InvariantCulture) ? (long)l : 0L,
                "decimal" or "money" => cellValue.TryConvert(out double dec, System.Globalization.CultureInfo.InvariantCulture) ? (decimal)dec : 0m,
                "double" or "float" => cellValue.TryConvert(out double f, System.Globalization.CultureInfo.InvariantCulture) ? f : 0.0,
                "bool" or "boolean" => cellValue.TryConvert(out bool b) && b,
                "datetime" or "date" => cellValue.TryConvert(out DateTime dt) ? dt : DateTime.MinValue,
                _ => cellValue.ToString()
            };
        }
    }
}
