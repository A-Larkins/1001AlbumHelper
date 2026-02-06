using ClosedXML.Excel;

namespace _1001AlbumHelper;

public class ExcelGenerator
{
    public void CreateAlbumSpreadsheet(List<Album> albums, string filename)
    {
        // Create output directory if it doesn't exist
        string outputDir = "output";
        Directory.CreateDirectory(outputDir);
        string fullPath = Path.Combine(outputDir, filename);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("1001 Albums");

        // Add legend at the top
        int currentRow = 1;

        // Row 1 - empty
        currentRow++;

        // Row 2 - Legend header and descriptions
        var starCell = worksheet.Cell(currentRow, 2);
        starCell.Value = "⭐";
        starCell.Style.Font.FontName = "Segoe UI Emoji";
        worksheet.Cell(currentRow, 3).Value = "Really enjoyable/Favs";
        worksheet.Cell(currentRow, 4).Value = "Only bangers and/or perfect albums.";
        currentRow++;

        // Row 3
        worksheet.Cell(currentRow, 2).Value = "👍";
        worksheet.Cell(currentRow, 3).Value = "Mostly enjoyable";
        worksheet.Cell(currentRow, 4).Value = "Good stuff, but just not my favorite, or just not a perfect album.";
        currentRow++;

        // Row 4
        worksheet.Cell(currentRow, 2).Value = "👎";
        worksheet.Cell(currentRow, 3).Value = "Mostly sucks";
        worksheet.Cell(currentRow, 4).Value = "Probably means the singer is bad.";
        currentRow++;

        // Row 5
        worksheet.Cell(currentRow, 2).Value = "❌";
        worksheet.Cell(currentRow, 3).Value = "Trash";
        worksheet.Cell(currentRow, 4).Value = "Actual torture to get through.";
        currentRow++;

        // Row 6 - empty
        currentRow++;

        // Row 7 - Headers
        worksheet.Cell(currentRow, 1).Value = "#";
        worksheet.Cell(currentRow, 2).Value = "Rating";
        worksheet.Cell(currentRow, 3).Value = "Album";
        worksheet.Cell(currentRow, 4).Value = "Artist";
        worksheet.Cell(currentRow, 5).Value = "Year";

        // Make headers bold
        worksheet.Range(currentRow, 1, currentRow, 5).Style.Font.Bold = true;
        currentRow++;

        // Add data with numbering
        for (int i = 0; i < albums.Count; i++)
        {
            worksheet.Cell(currentRow + i, 1).Value = i + 1; // Number
            // Column 2 (Rating) left blank
            worksheet.Cell(currentRow + i, 3).Value = albums[i].AlbumName; // Album
            worksheet.Cell(currentRow + i, 4).Value = albums[i].Artist; // Artist
            worksheet.Cell(currentRow + i, 5).Value = albums[i].Year; // Year
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        // Save the file
        workbook.SaveAs(fullPath);
        Console.WriteLine($"Excel file saved to: {fullPath}");
    }
}
