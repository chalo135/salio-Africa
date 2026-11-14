using System.Text;

namespace Salio.Infrastructure.Imports;

// A comma-separated file as RFC 4180 describes it: a field in double quotes
// may hold commas, line breaks and "" (one quote). Lines end in CRLF, LF or
// CR. UTF-8, with or without the byte-order mark Excel puts at the start.
public static class CsvReader
{
    public static List<ImportFileRow> Read(byte[] content)
    {
        // A byte that is not valid UTF-8 becomes the replacement character
        // rather than stopping the whole file.
        string text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(content);

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text.Substring(1);
        }

        var rows = new List<ImportFileRow>();
        var cells = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool rowHasContent = false;
        int rowNumber = 1;
        int i = 0;

        void EndField()
        {
            cells.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();

            if (!ImportFile.IsEmpty(cells))
            {
                rows.Add(new ImportFileRow { RowNumber = rowNumber, Cells = new List<string>(cells) });

                // Stop early on a huge file rather than read it all.
                if (rows.Count - 1 > ImportFile.MaxDataRows)
                {
                    throw new ImportFileException(ImportFile.TooManyRowsMessage);
                }
            }

            cells.Clear();
            rowNumber++;
            rowHasContent = false;
        }

        while (i < text.Length)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // "" inside quotes is one quote; a single " ends the field.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                rowHasContent = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                EndField();
                rowHasContent = true;
                i++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                EndRow();

                // CRLF is one line ending, not two.
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                i++;
                continue;
            }

            field.Append(c);
            rowHasContent = true;
            i++;
        }

        // The last line usually has no line ending after it.
        if (rowHasContent || field.Length > 0 || cells.Count > 0)
        {
            EndRow();
        }

        return rows;
    }
}
